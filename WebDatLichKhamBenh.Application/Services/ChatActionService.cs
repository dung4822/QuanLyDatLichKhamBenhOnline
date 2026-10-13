using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.Extensions.Logging;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Application.DTOs.Patients;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Extensions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Mappings;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Services;

// Thao tác qua chat theo mô hình "Đề xuất → Xác nhận":
// 1. Mô hình gọi công cụ propose_* → backend kiểm tra sơ bộ và tạo THẺ XÁC NHẬN (chưa đổi dữ liệu).
// 2. Người dùng bấm Xác nhận → backend gọi đúng service nghiệp vụ như khi thao tác trên giao diện.
// Mô hình không có công cụ nào ghi dữ liệu trực tiếp, và không bao giờ được truyền PatientId:
// bệnh nhân luôn lấy từ cookie đăng nhập.
public class ChatActionService : IChatActionService
{
    private readonly IChatRepository _chats;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IAppointmentService _appointments;
    private readonly IPatientService _patients;
    private readonly IAccountService _accounts;
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IValidator<UpdatePatientRequest> _profileValidator;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ChatActionService> _logger;

    public ChatActionService(
        IChatRepository chats,
        IAppointmentRepository appointmentRepository,
        IAppointmentService appointments,
        IPatientService patients,
        IAccountService accounts,
        IClinicScheduleTransaction transaction,
        IValidator<UpdatePatientRequest> profileValidator,
        ICurrentUser currentUser,
        ILogger<ChatActionService> logger)
    {
        _chats = chats;
        _appointmentRepository = appointmentRepository;
        _appointments = appointments;
        _patients = patients;
        _accounts = accounts;
        _transaction = transaction;
        _profileValidator = profileValidator;
        _currentUser = currentUser;
        _logger = logger;
    }

    // Thẻ xác nhận chỉ có hiệu lực trong 10 phút, vì giờ trống có thể bị người khác đặt mất.
    public static readonly TimeSpan ActionLifetime = TimeSpan.FromMinutes(10);

    private const string MyAppointmentsTool = "get_my_appointments";
    private const string BookTool = "propose_book_appointment";
    private const string CancelTool = "propose_cancel_appointment";
    private const string RescheduleTool = "propose_reschedule_appointment";
    private const string UpdateProfileTool = "propose_update_profile";

    private const int ResultLimit = 20;
    private const int FailureReasonMaxLength = 500;

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    private static readonly JsonSerializerOptions ArgumentOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    // Tham số do mô hình gửi lên.
    private record NoArguments;
    private record BookArguments(int? AppointmentSlotId, string? Reason);
    private record CancelArguments(int? AppointmentId, string? Reason);
    private record RescheduleArguments(int? AppointmentId, int? NewAppointmentSlotId);
    private record ProfileArguments(string? FullName, DateOnly? DateOfBirth, int? Gender,
        string? PhoneNumber, string? Email, string? Address);

    // Dữ liệu lưu vào PayloadJson, dùng để thực thi khi người dùng bấm Xác nhận.
    private record BookPayload(int AppointmentSlotId, string? Reason);
    private record CancelPayload(int AppointmentId, string? Reason);
    private record ReschedulePayload(int AppointmentId, int NewAppointmentSlotId);
    // ProfileVersion = thời điểm hồ sơ được sửa lần cuối, để phát hiện hồ sơ đã bị đổi ở nơi khác.
    private record ProfilePayload(UpdatePatientRequest Profile, DateTime ProfileVersion);

    public IReadOnlyList<LlmToolDefinition> Tools { get; } =
    [
        new(MyAppointmentsTool,
            "Xem các lịch hẹn sắp tới (đang xác nhận) của chính người dùng đang chat. Dùng để lấy appointmentId trước khi đề xuất hủy hoặc đổi lịch.",
            Schema("""
                { "type": "object", "properties": {}, "additionalProperties": false }
                """)),
        new(BookTool,
            "Tạo THẺ XÁC NHẬN đặt lịch khám cho người dùng đang chat. Công cụ KHÔNG đặt lịch: người dùng phải tự bấm Xác nhận trên thẻ. appointmentSlotId phải lấy từ get_available_slots.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "appointmentSlotId": { "type": "integer", "minimum": 1 },
                    "reason": { "type": "string", "maxLength": 1000, "description": "Lý do khám đúng như người dùng nói; bỏ trống nếu họ không nói." }
                  },
                  "required": ["appointmentSlotId"],
                  "additionalProperties": false
                }
                """)),
        new(CancelTool,
            "Tạo THẺ XÁC NHẬN hủy một lịch hẹn của người dùng. Công cụ KHÔNG hủy lịch. appointmentId phải lấy từ get_my_appointments.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "appointmentId": { "type": "integer", "minimum": 1 },
                    "reason": { "type": "string", "maxLength": 500, "description": "Lý do hủy nếu người dùng nói." }
                  },
                  "required": ["appointmentId"],
                  "additionalProperties": false
                }
                """)),
        new(RescheduleTool,
            "Tạo THẺ XÁC NHẬN đổi lịch hẹn sang khung giờ khác. Công cụ KHÔNG đổi lịch. appointmentId lấy từ get_my_appointments, newAppointmentSlotId lấy từ get_available_slots. Chỉ đổi được trước giờ khám ít nhất 2 tiếng.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "appointmentId": { "type": "integer", "minimum": 1 },
                    "newAppointmentSlotId": { "type": "integer", "minimum": 1 }
                  },
                  "required": ["appointmentId", "newAppointmentSlotId"],
                  "additionalProperties": false
                }
                """)),
        new(UpdateProfileTool,
            "Tạo THẺ XÁC NHẬN sửa hồ sơ bệnh nhân của người dùng đang chat. Công cụ KHÔNG sửa hồ sơ. Chỉ truyền các trường người dùng muốn đổi, giá trị đúng như người dùng cung cấp.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "fullName": { "type": "string", "maxLength": 100 },
                    "dateOfBirth": { "type": "string", "format": "date", "description": "yyyy-MM-dd" },
                    "gender": { "type": "integer", "enum": [0, 1], "description": "0 = Nam, 1 = Nữ" },
                    "phoneNumber": { "type": "string", "maxLength": 20 },
                    "email": { "type": "string", "maxLength": 50, "description": "Chuỗi rỗng để xóa email." },
                    "address": { "type": "string", "maxLength": 255, "description": "Chuỗi rỗng để xóa địa chỉ." }
                  },
                  "additionalProperties": false
                }
                """))
    ];

    public bool CanHandle(string toolName) => Tools.Any(tool => tool.Name == toolName);

    // ===================== Công cụ cho mô hình =====================

    public async Task<ChatToolResult> ExecuteAsync(LlmToolCall toolCall, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var patientId = RequirePatientId();
            return toolCall.Name switch
            {
                MyAppointmentsTool => await GetMyAppointmentsAsync(ReadArguments<NoArguments>(toolCall.Arguments)),
                BookTool => await ProposeBookingAsync(patientId, ReadArguments<BookArguments>(toolCall.Arguments)),
                CancelTool => await ProposeCancelAsync(patientId, ReadArguments<CancelArguments>(toolCall.Arguments)),
                RescheduleTool => await ProposeRescheduleAsync(patientId, ReadArguments<RescheduleArguments>(toolCall.Arguments)),
                UpdateProfileTool => await ProposeProfileUpdateAsync(patientId, ReadArguments<ProfileArguments>(toolCall.Arguments)),
                _ => new(Error(toolCall.Name, "unsupported_tool", "Công cụ này không được phép sử dụng."))
            };
        }
        catch (JsonException)
        {
            return new(InvalidArguments(toolCall.Name, "Tham số phải là JSON object đúng kiểu, không có thuộc tính lạ."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Không thể hoàn thành công cụ thao tác chat {ToolName}.", toolCall.Name);
            return new(Error(toolCall.Name, "action_unavailable", "Chưa thể xử lý yêu cầu lúc này. Vui lòng thử lại sau."));
        }
    }

    private async Task<ChatToolResult> GetMyAppointmentsAsync(NoArguments _)
    {
        // GetMineAsync tự giới hạn theo bệnh nhân đang đăng nhập.
        var page = await _appointments.GetMineAsync(new AppointmentQuery
        {
            FromDate = ClinicClock.Today, Status = AppointmentStatus.Confirmed, PageSize = ResultLimit
        });
        var now = ClinicClock.Now;
        // Không gửi lý do khám hay số điện thoại cho mô hình; chỉ thông tin cần để chọn lịch.
        var items = page.Items
            .Where(x => x.Date.ToDateTime(x.StartTime) > now)
            .Select(x => new
            {
                appointmentId = x.AppointmentId,
                doctorFullName = x.DoctorFullName,
                specialtyName = x.SpecialtyName,
                date = x.Date,
                startTime = x.StartTime,
                endTime = x.EndTime
            })
            .ToList();

        return new(Result(MyAppointmentsTool, new { items, hasMore = page.TotalCount > page.Items.Count }));
    }

    private async Task<ChatToolResult> ProposeBookingAsync(int patientId, BookArguments arguments)
    {
        if (arguments.AppointmentSlotId is null or <= 0)
            return new(InvalidArguments(BookTool, "appointmentSlotId là bắt buộc và phải là số nguyên dương."));
        if (arguments.Reason?.Length > 1000)
            return new(InvalidArguments(BookTool, "reason tối đa 1000 ký tự."));

        var slot = await _appointmentRepository.GetSlotAsync(arguments.AppointmentSlotId.Value);
        var problem = GetSlotProblem(slot);
        if (problem is not null)
            return new(Error(BookTool, "slot_unavailable", problem));
        if (await _appointmentRepository.HasPatientOverlapAsync(patientId, slot!.Date, slot.StartTime, slot.EndTime))
            return new(Error(BookTool, "patient_overlap", "Người dùng đã có lịch hẹn khác trùng khung giờ này."));

        var reason = arguments.Reason.TrimToNull();
        var preview = new ChatActionPreview("Đặt lịch khám",
        [
            new("Bác sĩ", null, slot.Doctor.FullName),
            new("Chuyên khoa", null, slot.Doctor.Specialty.Name),
            new("Ngày khám", null, FormatDate(slot.Date)),
            new("Giờ khám", null, FormatTime(slot.StartTime, slot.EndTime)),
            new("Lý do khám", null, reason ?? "Không ghi")
        ]);
        return Propose(BookTool, ChatActionType.BookAppointment,
            new BookPayload(slot.AppointmentSlotId, reason), preview, preview);
    }

    private async Task<ChatToolResult> ProposeCancelAsync(int patientId, CancelArguments arguments)
    {
        if (arguments.Reason?.Length > 500)
            return new(InvalidArguments(CancelTool, "reason tối đa 500 ký tự."));

        var (appointment, problem) = await GetOwnActiveAppointmentAsync(patientId, arguments.AppointmentId);
        if (problem is not null)
            return new(Error(CancelTool, "appointment_unavailable", problem));

        var reason = arguments.Reason.TrimToNull();
        var preview = new ChatActionPreview("Hủy lịch khám",
        [
            new("Bác sĩ", null, appointment!.DoctorFullName),
            new("Chuyên khoa", null, appointment.SpecialtyName),
            new("Ngày khám", null, FormatDate(appointment.Date)),
            new("Giờ khám", null, FormatTime(appointment.StartTime, appointment.EndTime)),
            new("Lý do hủy", null, reason ?? "Không ghi")
        ]);
        return Propose(CancelTool, ChatActionType.CancelAppointment,
            new CancelPayload(appointment.AppointmentId, reason), preview, preview);
    }

    private async Task<ChatToolResult> ProposeRescheduleAsync(int patientId, RescheduleArguments arguments)
    {
        if (arguments.NewAppointmentSlotId is null or <= 0)
            return new(InvalidArguments(RescheduleTool, "newAppointmentSlotId là bắt buộc và phải là số nguyên dương."));

        var (appointment, problem) = await GetOwnActiveAppointmentAsync(patientId, arguments.AppointmentId);
        if (problem is not null)
            return new(Error(RescheduleTool, "appointment_unavailable", problem));
        if (appointment!.Date.ToDateTime(appointment.StartTime) < ClinicClock.Now.Add(ClinicSchedule.RescheduleDeadline))
            return new(Error(RescheduleTool, "reschedule_deadline", "Chỉ được đổi lịch trước giờ khám ít nhất 2 tiếng."));
        if (appointment.AppointmentSlotId == arguments.NewAppointmentSlotId)
            return new(Error(RescheduleTool, "same_slot", "Khung giờ mới trùng với lịch hiện tại."));

        var slot = await _appointmentRepository.GetSlotAsync(arguments.NewAppointmentSlotId.Value);
        var slotProblem = GetSlotProblem(slot);
        if (slotProblem is not null)
            return new(Error(RescheduleTool, "slot_unavailable", slotProblem));
        if (await _appointmentRepository.HasPatientOverlapAsync(patientId, slot!.Date, slot.StartTime, slot.EndTime,
                excludeAppointmentId: appointment.AppointmentId))
            return new(Error(RescheduleTool, "patient_overlap", "Người dùng đã có lịch hẹn khác trùng khung giờ mới."));

        var preview = new ChatActionPreview("Đổi lịch khám",
        [
            new("Bác sĩ", appointment.DoctorFullName, slot.Doctor.FullName),
            new("Chuyên khoa", appointment.SpecialtyName, slot.Doctor.Specialty.Name),
            new("Ngày khám", FormatDate(appointment.Date), FormatDate(slot.Date)),
            new("Giờ khám", FormatTime(appointment.StartTime, appointment.EndTime), FormatTime(slot.StartTime, slot.EndTime))
        ]);
        return Propose(RescheduleTool, ChatActionType.RescheduleAppointment,
            new ReschedulePayload(appointment.AppointmentId, slot.AppointmentSlotId), preview, preview);
    }

    private async Task<ChatToolResult> ProposeProfileUpdateAsync(int patientId, ProfileArguments arguments)
    {
        var current = await _patients.GetByIdAsync(patientId);
        if (current is null)
            return new(Error(UpdateProfileTool, "profile_not_found", "Hồ sơ bệnh nhân không tồn tại."));

        // Trường nào mô hình không gửi thì giữ nguyên giá trị hiện tại.
        var profile = new UpdatePatientRequest
        {
            FullName = arguments.FullName?.Trim() ?? current.FullName,
            DateOfBirth = arguments.DateOfBirth ?? current.DateOfBirth,
            Gender = arguments.Gender ?? current.Gender,
            PhoneNumber = arguments.PhoneNumber?.Trim() ?? current.PhoneNumber,
            Email = arguments.Email is null ? current.Email : arguments.Email.TrimToNull(),
            Address = arguments.Address is null ? current.Address : arguments.Address.TrimToNull()
        };
        var validation = await _profileValidator.ValidateAsync(profile);
        if (!validation.IsValid)
            return new(InvalidArguments(UpdateProfileTool, string.Join(" ", validation.Errors.Select(e => e.ErrorMessage))));

        var changes = new List<ChatActionDetail>();
        AddChange(changes, "Họ tên", current.FullName, profile.FullName);
        AddChange(changes, "Ngày sinh", FormatBirthDate(current.DateOfBirth), FormatBirthDate(profile.DateOfBirth));
        AddChange(changes, "Giới tính", FormatGender(current.Gender), FormatGender(profile.Gender));
        AddChange(changes, "Số điện thoại", current.PhoneNumber, profile.PhoneNumber);
        AddChange(changes, "Email", current.Email ?? "(trống)", profile.Email ?? "(trống)");
        AddChange(changes, "Địa chỉ", current.Address ?? "(trống)", profile.Address ?? "(trống)");
        if (changes.Count == 0)
            return new(Error(UpdateProfileTool, "no_changes", "Thông tin mới giống hệt hồ sơ hiện tại."));

        var preview = new ChatActionPreview("Cập nhật hồ sơ", changes);
        // Giá trị cũ chỉ hiển thị cho người dùng trên thẻ, không gửi cho mô hình.
        var modelSummary = new { title = preview.Title, changes = changes.Select(x => new { x.Label, newValue = x.Value }) };
        return Propose(UpdateProfileTool, ChatActionType.UpdateProfile,
            new ProfilePayload(profile, current.UpdatedAt ?? current.CreatedAt), preview, modelSummary);
    }

    // Kiểm tra sơ bộ để báo lỗi sớm. AppointmentService vẫn kiểm tra lại ĐẦY ĐỦ lúc người dùng xác nhận
    // (ca làm việc, ngày nghỉ, trùng lịch bác sĩ...), vì dữ liệu có thể thay đổi trong lúc chờ bấm nút.
    private static string? GetSlotProblem(AppointmentSlot? slot)
    {
        if (slot is null)
            return "Khung giờ khám không tồn tại.";
        if (slot.Doctor.IsDelete || !SpecialtyStatus.IsActive(slot.Doctor.Specialty.Status))
            return "Bác sĩ không còn nhận lịch khám.";
        if (slot.Status != AppointmentSlotStatus.Available)
            return "Khung giờ không còn trống. Vui lòng chọn giờ khác.";
        if (slot.Date.ToDateTime(slot.StartTime) <= ClinicClock.Now || slot.Date > ClinicSchedule.LastBookableDate)
            return "Khung giờ phải chưa bắt đầu và nằm trong 7 ngày từ hôm nay.";
        return null;
    }

    // Lịch của người khác cũng báo "không tồn tại", để không lộ lịch đó có thật hay không.
    private async Task<(Appointment? Appointment, string? Problem)> GetOwnActiveAppointmentAsync(int patientId, int? appointmentId)
    {
        if (appointmentId is null or <= 0)
            return (null, "appointmentId là bắt buộc và phải là số nguyên dương.");

        var appointment = await _appointmentRepository.GetByIdAsync(appointmentId.Value);
        if (appointment is null || appointment.PatientId != patientId)
            return (null, "Lịch hẹn không tồn tại.");
        if (appointment.Status != AppointmentStatus.Confirmed)
            return (null, "Chỉ có thể hủy hoặc đổi lịch đang được xác nhận.");
        if (appointment.Date.ToDateTime(appointment.StartTime) <= ClinicClock.Now)
            return (null, "Lịch hẹn đã đến giờ khám.");
        return (appointment, null);
    }

    // Tạo đề xuất (chưa lưu). ChatService sẽ lưu cùng lượt chat để không có đề xuất "mồ côi".
    private static ChatToolResult Propose(string tool, ChatActionType type, object payload,
        ChatActionPreview preview, object modelSummary)
    {
        var action = new ChatAction
        {
            Type = type,
            PayloadJson = JsonSerializer.Serialize(payload),
            PreviewJson = JsonSerializer.Serialize(preview),
            ExpiresAt = DateTime.UtcNow.Add(ActionLifetime)
        };
        var result = Result(tool, new
        {
            status = "awaiting_user_confirmation",
            message = "Đã tạo thẻ xác nhận. Thao tác CHƯA được thực hiện; người dùng phải tự bấm Xác nhận trên thẻ.",
            summary = modelSummary
        });
        return new(result, action);
    }

    // ===================== Người dùng xác nhận / hủy =====================

    public async Task<ChatActionDto> ConfirmAsync(Guid actionId, CancellationToken cancellationToken = default)
    {
        var patientId = RequirePatientId();
        var action = await GetOwnActionAsync(actionId, patientId, tracking: true);

        // Bấm lại sau khi đã thành công (ví dụ lần trước mất response): trả lại kết quả cũ, không làm lại.
        if (action.Status == ChatActionStatus.Confirmed)
            return action.ToDto();
        await EnsurePendingAsync(action);

        var completed = false;
        try
        {
            await _transaction.ExecuteAsync(async () =>
            {
                // Transaction có khóa xếp hàng các request ghi lịch; request đến sau sẽ thấy đề xuất không còn Pending.
                if (!await _chats.TryCompleteAsync(actionId, ChatActionStatus.Confirmed))
                    return;

                var result = await ExecuteActionAsync(action, patientId, cancellationToken);
                action.Status = ChatActionStatus.Confirmed;
                action.CompletedAt = DateTime.UtcNow;
                action.ResultJson = JsonSerializer.Serialize(result);
                await _chats.SaveChangesAsync();
                completed = true;
            }, cancellationToken);
        }
        catch (Exception exception) when (IsBusinessFailure(exception))
        {
            // Nghiệp vụ từ chối (slot vừa bị đặt, quá hạn đổi lịch...): transaction đã rollback,
            // đánh dấu Failed để thẻ hiện lý do, rồi trả lỗi như API thường.
            await _chats.TryCompleteAsync(actionId, ChatActionStatus.Failed, GetFailureMessage(exception));
            throw;
        }

        if (completed)
            return action.ToDto();

        // Một request khác đã xử lý đề xuất này trước (ví dụ người dùng bấm hai lần).
        var latest = await GetOwnActionAsync(actionId, patientId, tracking: false);
        if (latest.Status == ChatActionStatus.Confirmed)
            return latest.ToDto();
        throw new ChatActionConflictException(GetClosedMessage(latest));
    }

    public async Task<ChatActionDto> RejectAsync(Guid actionId)
    {
        var patientId = RequirePatientId();
        var action = await GetOwnActionAsync(actionId, patientId, tracking: false);
        if (action.Status == ChatActionStatus.Rejected)
            return action.ToDto();
        await EnsurePendingAsync(action);

        await _chats.TryCompleteAsync(actionId, ChatActionStatus.Rejected);
        var latest = await GetOwnActionAsync(actionId, patientId, tracking: false);
        if (latest.Status != ChatActionStatus.Rejected)
            throw new ChatActionConflictException(GetClosedMessage(latest));
        return latest.ToDto();
    }

    private async Task<object> ExecuteActionAsync(ChatAction action, int patientId, CancellationToken cancellationToken)
        => action.Type switch
        {
            ChatActionType.BookAppointment => await BookAsync(ReadPayload<BookPayload>(action), patientId, cancellationToken),
            ChatActionType.CancelAppointment => await CancelAsync(ReadPayload<CancelPayload>(action), cancellationToken),
            ChatActionType.RescheduleAppointment => await RescheduleAsync(ReadPayload<ReschedulePayload>(action), cancellationToken),
            ChatActionType.UpdateProfile => await UpdateProfileAsync(ReadPayload<ProfilePayload>(action), patientId),
            _ => throw new InvalidOperationException($"Loại đề xuất {action.Type} chưa được hỗ trợ.")
        };

    // Các hàm dưới gọi service nghiệp vụ sẵn có; mọi kiểm tra quyền và quy tắc lịch khám vẫn chạy ở đó.
    private async Task<object> BookAsync(BookPayload payload, int patientId, CancellationToken cancellationToken)
        => AppointmentResult(await _appointments.CreateAsync(new CreateAppointmentRequest
        {
            PatientId = patientId, AppointmentSlotId = payload.AppointmentSlotId, Reason = payload.Reason
        }, cancellationToken));

    private async Task<object> CancelAsync(CancelPayload payload, CancellationToken cancellationToken)
        => AppointmentResult(await _appointments.CancelAsync(payload.AppointmentId,
            new CancelAppointmentRequest { Reason = payload.Reason }, cancellationToken));

    private async Task<object> RescheduleAsync(ReschedulePayload payload, CancellationToken cancellationToken)
        => AppointmentResult(await _appointments.RescheduleAsync(payload.AppointmentId,
            new RescheduleAppointmentRequest { AppointmentSlotId = payload.NewAppointmentSlotId }, cancellationToken));

    private async Task<object> UpdateProfileAsync(ProfilePayload payload, int patientId)
    {
        var current = await _patients.GetByIdAsync(patientId)
            ?? throw new ResourceNotFoundException("Hồ sơ bệnh nhân không tồn tại.");
        // Hồ sơ đã bị sửa ở nơi khác sau khi tạo thẻ: không ghi đè bằng dữ liệu cũ trên thẻ.
        if ((current.UpdatedAt ?? current.CreatedAt) != payload.ProfileVersion)
            throw new ChatActionConflictException("Hồ sơ đã được thay đổi sau khi tạo đề xuất. Vui lòng nhờ trợ lý tạo đề xuất mới.");

        var updated = await _accounts.UpdateMyPatientProfileAsync(payload.Profile);
        return new { patientId = updated.PatientId, updatedAt = updated.UpdatedAt };
    }

    private static object AppointmentResult(AppointmentDto x) => new
    {
        appointmentId = x.AppointmentId,
        doctorFullName = x.DoctorFullName,
        specialtyName = x.SpecialtyName,
        date = x.Date,
        startTime = x.StartTime,
        endTime = x.EndTime,
        status = x.Status.ToString()
    };

    private async Task EnsurePendingAsync(ChatAction action)
    {
        if (action.Status != ChatActionStatus.Pending)
            throw new ChatActionConflictException(GetClosedMessage(action));
        if (action.ExpiresAt <= DateTime.UtcNow)
        {
            await _chats.TryCompleteAsync(action.ChatActionId, ChatActionStatus.Expired);
            throw new ChatActionConflictException(GetClosedMessage(action));
        }
    }

    private static string GetClosedMessage(ChatAction action) => action.GetCurrentStatus() switch
    {
        ChatActionStatus.Confirmed => "Đề xuất này đã được thực hiện.",
        ChatActionStatus.Rejected => "Bạn đã hủy đề xuất này.",
        ChatActionStatus.Expired => "Đề xuất đã hết hạn sau 10 phút. Vui lòng nhờ trợ lý tạo đề xuất mới.",
        ChatActionStatus.Superseded => "Đề xuất này đã được thay bằng đề xuất mới hơn.",
        ChatActionStatus.Failed => $"Đề xuất đã thất bại: {action.FailureReason}",
        _ => "Đề xuất đang được xử lý. Vui lòng tải lại cuộc trò chuyện."
    };

    // Lỗi nghiệp vụ có message viết sẵn cho người dùng. Lỗi hệ thống (DB, mạng...) thì giữ Pending để thử lại.
    private static bool IsBusinessFailure(Exception exception) => exception is AppointmentConflictException
        or ResourceNotFoundException or AccessDeniedException or ValidationException or ChatActionConflictException;

    private static string GetFailureMessage(Exception exception)
    {
        var message = exception is ValidationException validation
            ? string.Join(" ", validation.Errors.Select(e => e.ErrorMessage))
            : exception.Message;
        return message.Length <= FailureReasonMaxLength ? message : message[..FailureReasonMaxLength];
    }

    // ===================== Tiện ích =====================

    private int RequirePatientId()
        => _currentUser.Role == AccountRole.Patient && _currentUser.PatientId is { } patientId
            ? patientId
            : throw new AccessDeniedException();

    private async Task<ChatAction> GetOwnActionAsync(Guid actionId, int patientId, bool tracking)
        => await _chats.GetActionAsync(actionId, patientId, tracking)
            ?? throw new ResourceNotFoundException("Đề xuất không tồn tại.");

    private static void AddChange(List<ChatActionDetail> changes, string label, string before, string after)
    {
        if (before != after)
            changes.Add(new ChatActionDetail(label, before, after));
    }

    private static string FormatDate(DateOnly date) => date.ToString("dddd, dd/MM/yyyy", Vietnamese);
    private static string FormatBirthDate(DateOnly date) => date.ToString("dd/MM/yyyy", Vietnamese);
    private static string FormatTime(TimeOnly start, TimeOnly end) => $"{start:HH\\:mm} - {end:HH\\:mm}";
    private static string FormatGender(int gender) => gender == 1 ? "Nữ" : "Nam";

    private static T ReadArguments<T>(string json)
        => JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(json) ? "{}" : json, ArgumentOptions)
            ?? throw new JsonException("Tham số rỗng.");

    private static T ReadPayload<T>(ChatAction action) => JsonSerializer.Deserialize<T>(action.PayloadJson)!;

    private static JsonElement Schema(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static ChatLookupResult Result(string tool, object data)
        => new(tool, JsonSerializer.SerializeToElement(data));

    private static ChatLookupResult Error(string tool, string code, string message)
        => Result(tool, new { error = new { code, message } });

    private static ChatLookupResult InvalidArguments(string tool, string message)
        => Error(tool, "invalid_arguments", message);
}
