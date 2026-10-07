using FluentValidation;
using System.Text.Json;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Extensions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Application.Validators;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Services;

// Vòng đời lịch hẹn: Đặt (Confirmed) → Đổi lịch / Hủy (Cancelled) / Hoàn thành (Completed) / Vắng mặt (NoShow).
// Mọi thao tác ghi đều chạy trong IClinicScheduleTransaction để hai người không đặt trùng một khung giờ.
public class AppointmentService : IAppointmentService
{
    private readonly IAppointmentRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly IPatientRepository _patients;
    private readonly INonWorkingDayRepository _nonWorkingDays;
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IValidator<CreateAppointmentRequest> _createValidator;
    private readonly IValidator<CancelAppointmentRequest> _cancelValidator;
    private readonly IValidator<AppointmentQuery> _queryValidator;
    private readonly IValidator<RescheduleAppointmentRequest> _rescheduleValidator;

    public AppointmentService(IAppointmentRepository repository, IPatientRepository patients,
        INonWorkingDayRepository nonWorkingDays, IClinicScheduleTransaction transaction,
        IValidator<CreateAppointmentRequest> createValidator, IValidator<CancelAppointmentRequest> cancelValidator,
        IValidator<AppointmentQuery> queryValidator, IValidator<RescheduleAppointmentRequest> rescheduleValidator,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
        _patients = patients;
        _nonWorkingDays = nonWorkingDays;
        _transaction = transaction;
        _createValidator = createValidator;
        _cancelValidator = cancelValidator;
        _queryValidator = queryValidator;
        _rescheduleValidator = rescheduleValidator;
    }

    // ===================== Đọc =====================

    public async Task<AppointmentPageDto> GetAllAsync(AppointmentQuery query)
    {
        await _queryValidator.ValidateAndThrowAsync(query);
        ApplyAccessScope(query);
        var (items, totalCount) = await _repository.GetAllAsync(query);
        return new AppointmentPageDto
        {
            Items = items.Select(MapToDto).ToList(), TotalCount = totalCount,
            Page = query.Page, PageSize = query.PageSize
        };
    }

    public Task<AppointmentPageDto> GetMineAsync(AppointmentQuery query)
    {
        if (_currentUser.Role is not (AccountRole.Patient or AccountRole.Doctor))
            throw new AccessDeniedException();
        return GetAllAsync(query);
    }

    public async Task<AppointmentDto?> GetByIdAsync(int id)
    {
        var appointment = await _repository.GetByIdAsync(id);
        if (appointment is null)
            return null;

        EnsureCanRead(appointment);
        return MapToDto(appointment);
    }

    public async Task<List<AppointmentHistoryDto>> GetHistoryAsync(int id, int page = 1, int pageSize = 50)
    {
        var appointment = await _repository.GetByIdAsync(id)
            ?? throw new ResourceNotFoundException("Lịch hẹn không tồn tại.");
        EnsureCanRead(appointment);
        PagingRules.EnsureValid(page, pageSize);

        var history = await _repository.GetHistoryAsync(id, page, pageSize);
        return history.Select(x => new AppointmentHistoryDto(
            x.AppointmentHistoryId, x.AppointmentId, x.ActorAccountId, x.ActorDisplayName, x.ActorRole, x.Action,
            x.BeforeJson is null ? null : JsonSerializer.Deserialize<AppointmentSnapshot>(x.BeforeJson),
            JsonSerializer.Deserialize<AppointmentSnapshot>(x.AfterJson)!, x.OccurredAt)).ToList();
    }

    // ===================== Ghi =====================

    public async Task<AppointmentDto> CreateAsync(CreateAppointmentRequest request, CancellationToken cancellationToken = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        if (!_currentUser.IsStaff() && !_currentUser.IsPatient(request.PatientId))
            throw new AccessDeniedException();

        return await _transaction.ExecuteAsync(async () =>
        {
            var patient = await GetRequiredPatientAsync(request.PatientId);
            var slot = await GetRequiredSlotAsync(request.AppointmentSlotId);
            await EnsureSlotCanBeBookedAsync(slot, patient.PatientId);
            await ReserveSlotAsync(slot, "Đã đến giờ bắt đầu khám, không thể đặt khung giờ này.");

            var appointment = new Appointment
            {
                PatientId = patient.PatientId,
                PatientFullName = patient.FullName,
                PatientPhoneNumber = patient.PhoneNumber,
                Reason = request.Reason.TrimToNull(),
                Status = AppointmentStatus.Confirmed,
                CreatedAt = DateTime.UtcNow
            };
            CopySlotInfo(appointment, slot);

            await _repository.AddAsync(appointment);
            await RecordHistoryAsync(appointment, "Created", before: null);
            await _repository.SaveChangesAsync();
            return MapToDto(appointment);
        }, cancellationToken);
    }

    public async Task<AppointmentDto> RescheduleAsync(int id, RescheduleAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        await _rescheduleValidator.ValidateAndThrowAsync(request, cancellationToken);

        return await _transaction.ExecuteAsync(async () =>
        {
            var appointment = await GetRequiredTrackingAsync(id);
            EnsureCanManage(appointment);
            if (appointment.Status != AppointmentStatus.Confirmed)
                throw new AppointmentConflictException("Chỉ có thể đổi lịch đang được xác nhận.");
            EnsureRescheduleDeadline(appointment);

            // Gửi lại đúng khung giờ đang giữ thì không làm gì (tránh nhả rồi đặt lại chính nó).
            if (appointment.AppointmentSlotId == request.AppointmentSlotId)
                return MapToDto(appointment);

            await GetRequiredPatientAsync(appointment.PatientId);
            var newSlot = await GetRequiredSlotAsync(request.AppointmentSlotId);
            // Bỏ qua chính lịch hẹn này khi kiểm tra trùng giờ.
            await EnsureSlotCanBeBookedAsync(newSlot, appointment.PatientId, excludeAppointmentId: id);

            // Kiểm tra lại hạn đổi lịch sau các truy vấn, phòng trường hợp vừa qua mốc 2 tiếng.
            EnsureRescheduleDeadline(appointment);
            await ReserveSlotAsync(newSlot, "Đã đến giờ bắt đầu khám, không thể đổi sang khung giờ này.");

            var oldSlotId = appointment.AppointmentSlotId;
            var before = Snapshot(appointment);
            CopySlotInfo(appointment, newSlot);
            appointment.UpdatedAt = DateTime.UtcNow;
            await RecordHistoryAsync(appointment, "Rescheduled", before);
            await _repository.SaveChangesAsync();

            await ReleaseSlotAsync(oldSlotId);
            return MapToDto(appointment);
        }, cancellationToken);
    }

    public async Task<AppointmentDto> CancelAsync(int id, CancelAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        await _cancelValidator.ValidateAndThrowAsync(request, cancellationToken);

        return await _transaction.ExecuteAsync(async () =>
        {
            var appointment = await GetRequiredTrackingAsync(id);
            EnsureCanManage(appointment);

            // Gọi hủy lần 2 (retry) thì trả kết quả cũ, không được mở lại slot mà người khác có thể đã đặt.
            if (appointment.Status == AppointmentStatus.Cancelled)
                return MapToDto(appointment);
            if (appointment.Status != AppointmentStatus.Confirmed)
                throw new AppointmentConflictException("Chỉ có thể hủy lịch đang được xác nhận.");
            if (appointment.Date.ToDateTime(appointment.StartTime) <= ClinicClock.Now)
                throw new AppointmentConflictException("Không thể hủy lịch đã đến giờ khám.");

            var before = Snapshot(appointment);
            appointment.Status = AppointmentStatus.Cancelled;
            appointment.CancelledAt = DateTime.UtcNow;
            appointment.UpdatedAt = appointment.CancelledAt;
            appointment.CancellationReason = request.Reason.TrimToNull();
            await RecordHistoryAsync(appointment, "Cancelled", before);
            await _repository.SaveChangesAsync();

            await ReleaseSlotAsync(appointment.AppointmentSlotId);
            return MapToDto(appointment);
        }, cancellationToken);
    }

    public Task<AppointmentDto> CompleteAsync(int id, CancellationToken cancellationToken = default)
        => FinishAsync(id, AppointmentStatus.Completed, cancellationToken);

    public Task<AppointmentDto> MarkNoShowAsync(int id, CancellationToken cancellationToken = default)
        => FinishAsync(id, AppointmentStatus.NoShow, cancellationToken);

    // Kết thúc lịch hẹn sau giờ khám: Completed (đã khám) hoặc NoShow (bệnh nhân không đến).
    private Task<AppointmentDto> FinishAsync(int id, AppointmentStatus status, CancellationToken cancellationToken)
        => _transaction.ExecuteAsync(async () =>
        {
            var appointment = await GetRequiredTrackingAsync(id);
            if (!_currentUser.IsStaff() && !_currentUser.IsDoctor(appointment.DoctorId))
                throw new AccessDeniedException();
            if (appointment.Status == status)
                return MapToDto(appointment);
            if (appointment.Status != AppointmentStatus.Confirmed)
                throw new AppointmentConflictException("Chỉ có thể kết thúc lịch đang được xác nhận.");
            if (appointment.Date.ToDateTime(appointment.EndTime) > ClinicClock.Now)
                throw new AppointmentConflictException("Chỉ cập nhật kết quả sau giờ kết thúc của lịch khám.");

            var before = Snapshot(appointment);
            appointment.Status = status;
            appointment.UpdatedAt = DateTime.UtcNow;
            await RecordHistoryAsync(appointment, status.ToString(), before);
            await _repository.SaveChangesAsync();
            return MapToDto(appointment);
        }, cancellationToken);

    // ===================== Khung giờ (slot) =====================

    // Các điều kiện để một khung giờ nhận lịch hẹn; dùng chung cho đặt mới và đổi lịch.
    private async Task EnsureSlotCanBeBookedAsync(AppointmentSlot slot, int patientId, int? excludeAppointmentId = null)
    {
        var unavailableReason = await GetUnavailableReasonAsync(slot);
        if (unavailableReason is not null)
            throw new AppointmentConflictException(unavailableReason);
        if (slot.Status != AppointmentSlotStatus.Available)
            throw new AppointmentConflictException("Khung giờ không còn trống. Vui lòng chọn giờ khác.");
        if (await _repository.HasDoctorOverlapAsync(slot.DoctorId, slot.Date, slot.StartTime, slot.EndTime, excludeAppointmentId))
            throw new AppointmentConflictException("Bác sĩ đã có lịch hẹn trong khoảng giờ này.");
        if (await _repository.HasPatientOverlapAsync(patientId, slot.Date, slot.StartTime, slot.EndTime, excludeAppointmentId))
            throw new AppointmentConflictException("Bệnh nhân đã có lịch hẹn trong khoảng giờ này.");
    }

    // Giữ chỗ: kiểm tra lần cuối giờ khám chưa bắt đầu, rồi chuyển slot sang Booked.
    // TryBookSlotAsync chỉ thành công nếu slot vẫn đang Available ngay trong câu UPDATE.
    private async Task ReserveSlotAsync(AppointmentSlot slot, string alreadyStartedMessage)
    {
        if (slot.Date.ToDateTime(slot.StartTime) <= ClinicClock.Now)
            throw new AppointmentConflictException(alreadyStartedMessage);
        if (!await _repository.TryBookSlotAsync(slot.AppointmentSlotId))
            throw new AppointmentConflictException("Khung giờ vừa được đặt. Vui lòng chọn giờ khác.");
    }

    // Mở lại slot sau khi hủy hoặc đổi lịch. Nếu slot không còn hợp lệ (bác sĩ nghỉ, ca bị xóa,
    // trùng lịch khác...) thì để Unavailable kèm lý do thay vì mở cho người khác đặt.
    private async Task ReleaseSlotAsync(int slotId)
    {
        var slot = await GetRequiredSlotAsync(slotId);
        var reason = await GetUnavailableReasonAsync(slot);
        if (reason is null && await _repository.HasDoctorOverlapAsync(slot.DoctorId, slot.Date, slot.StartTime, slot.EndTime))
            reason = "Bác sĩ có lịch hẹn khác trong khoảng giờ này.";
        await _repository.ReleaseSlotAsync(slotId, reason);
    }

    // Lịch hẹn lưu bản sao thông tin bác sĩ/khoa/giờ tại thời điểm đặt, để sau này đổi tên vẫn giữ đúng lịch sử.
    private static void CopySlotInfo(Appointment appointment, AppointmentSlot slot)
    {
        appointment.AppointmentSlotId = slot.AppointmentSlotId;
        appointment.DoctorId = slot.DoctorId;
        appointment.DoctorFullName = slot.Doctor.FullName;
        appointment.SpecialtyId = slot.Doctor.SpecialtyId;
        appointment.SpecialtyName = slot.Doctor.Specialty.Name;
        appointment.Date = slot.Date;
        appointment.StartTime = slot.StartTime;
        appointment.EndTime = slot.EndTime;
    }

    // Trả về lý do slot KHÔNG được đặt, hoặc null nếu slot hợp lệ.
    private async Task<string?> GetUnavailableReasonAsync(AppointmentSlot slot)
    {
        if (slot.Doctor.IsDelete)
            return "Bác sĩ không còn nhận lịch khám.";
        if (!SpecialtyStatus.IsActive(slot.Doctor.Specialty.Status))
            return "Chuyên khoa không còn hoạt động.";

        var now = ClinicClock.Now;
        var today = DateOnly.FromDateTime(now);
        if (slot.Date < today || slot.Date > today.AddDays(ClinicSchedule.BookingWindowDays - 1) ||
            slot.Date.ToDateTime(slot.StartTime) <= now)
            return "Khung giờ phải chưa bắt đầu và nằm trong 7 ngày từ hôm nay.";

        if (!IsStillInWorkingShift(slot))
            return "Khung giờ không còn thuộc lịch làm việc hiện tại.";

        if (await _nonWorkingDays.IsDoctorUnavailableAsync(slot.DoctorId, slot.Date))
            return "Bác sĩ hoặc bệnh viện nghỉ vào ngày này.";
        return null;
    }

    // Slot được sinh từ ca làm việc; nếu ca đã bị sửa/xóa sau đó thì slot cũ có thể không còn khớp.
    private static bool IsStillInWorkingShift(AppointmentSlot slot)
    {
        var assignment = slot.DoctorShift;
        var shift = assignment.Shift;
        return !assignment.IsDeleted && assignment.DoctorId == slot.DoctorId &&
            !shift.IsDeleted && shift.IsActive && shift.DayOfWeek == slot.Date.DayOfWeek &&
            slot.StartTime >= shift.StartTime && slot.EndTime <= shift.EndTime &&
            (slot.EndTime - slot.StartTime).TotalMinutes == ClinicSchedule.SlotMinutes &&
            (slot.StartTime - shift.StartTime).TotalMinutes % ClinicSchedule.SlotMinutes == 0;
    }

    // ===================== Phân quyền & tiện ích =====================

    // Bệnh nhân/bác sĩ chỉ được xem lịch của chính mình: ép bộ lọc theo người đang đăng nhập.
    private void ApplyAccessScope(AppointmentQuery query)
    {
        if (_currentUser.IsStaff())
            return;

        if (_currentUser.Role == AccountRole.Patient && _currentUser.PatientId.HasValue)
        {
            if (query.PatientId.HasValue && query.PatientId != _currentUser.PatientId)
                throw new AccessDeniedException();
            query.PatientId = _currentUser.PatientId;
        }
        else if (_currentUser.Role == AccountRole.Doctor && _currentUser.DoctorId.HasValue)
        {
            if (query.DoctorId.HasValue && query.DoctorId != _currentUser.DoctorId)
                throw new AccessDeniedException();
            query.DoctorId = _currentUser.DoctorId;
        }
        else
        {
            throw new AccessDeniedException();
        }
    }

    private void EnsureCanRead(Appointment appointment)
    {
        if (_currentUser.IsStaff() || _currentUser.IsPatient(appointment.PatientId) || _currentUser.IsDoctor(appointment.DoctorId))
            return;
        // Trả 404 thay vì 403 để không tiết lộ lịch của người khác có tồn tại hay không.
        throw new ResourceNotFoundException("Lịch hẹn không tồn tại.");
    }

    private void EnsureCanManage(Appointment appointment)
    {
        if (!_currentUser.IsStaff() && !_currentUser.IsPatient(appointment.PatientId))
            throw new AccessDeniedException();
    }

    private static void EnsureRescheduleDeadline(Appointment appointment)
    {
        if (appointment.Date.ToDateTime(appointment.StartTime) < ClinicClock.Now.Add(ClinicSchedule.RescheduleDeadline))
            throw new AppointmentConflictException("Chỉ được đổi lịch trước giờ khám ít nhất 2 tiếng.");
    }

    private async Task<Appointment> GetRequiredTrackingAsync(int id)
    {
        var appointment = await _repository.GetByIdAsync(id, tracking: true)
            ?? throw new ResourceNotFoundException("Lịch hẹn không tồn tại.");
        EnsureCanRead(appointment);
        return appointment;
    }

    private async Task<Patient> GetRequiredPatientAsync(int patientId)
        => await _patients.GetByIdAsync(patientId)
            ?? throw new ResourceNotFoundException("Bệnh nhân không tồn tại hoặc đã bị xóa.");

    private async Task<AppointmentSlot> GetRequiredSlotAsync(int slotId)
        => await _repository.GetSlotAsync(slotId)
            ?? throw new ResourceNotFoundException("Khung giờ khám không tồn tại.");

    // Ghi lại ai đã làm gì, trạng thái trước/sau, để tra cứu khi có tranh chấp.
    private Task RecordHistoryAsync(Appointment appointment, string action, AppointmentSnapshot? before)
        => _repository.AddHistoryAsync(new AppointmentHistory
        {
            Appointment = appointment,
            ActorAccountId = _currentUser.AccountId,
            ActorDisplayName = _currentUser.DisplayName,
            ActorRole = _currentUser.Role.ToString(),
            Action = action,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = JsonSerializer.Serialize(Snapshot(appointment)),
            OccurredAt = DateTime.UtcNow
        });

    private static AppointmentSnapshot Snapshot(Appointment x) => new(x.AppointmentSlotId, x.DoctorId,
        x.DoctorFullName, x.SpecialtyId, x.SpecialtyName, x.Date, x.StartTime, x.EndTime, x.Status, x.CancellationReason);

    private static AppointmentDto MapToDto(Appointment x) => new()
    {
        AppointmentId = x.AppointmentId, PatientId = x.PatientId,
        PatientFullName = x.PatientFullName, PatientPhoneNumber = x.PatientPhoneNumber,
        AppointmentSlotId = x.AppointmentSlotId, DoctorId = x.DoctorId,
        DoctorFullName = x.DoctorFullName, SpecialtyId = x.SpecialtyId,
        SpecialtyName = x.SpecialtyName, Date = x.Date, StartTime = x.StartTime,
        EndTime = x.EndTime, Reason = x.Reason, Status = x.Status,
        CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt,
        CancelledAt = x.CancelledAt, CancellationReason = x.CancellationReason
    };
}
