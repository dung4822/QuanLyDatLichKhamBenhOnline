using FluentValidation;
using System.Text.Json;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Services;

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

    public async Task<AppointmentDto?> GetByIdAsync(int id)
    {
        var appointment = await _repository.GetByIdAsync(id);
        if (appointment is not null) EnsureCanRead(appointment);
        return appointment is null ? null : MapToDto(appointment);
    }

    public async Task<AppointmentDto> CreateAsync(CreateAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        if (!IsStaff && (_currentUser.Role != AccountRole.Patient || _currentUser.PatientId != request.PatientId))
            throw new AccessDeniedException();
        return await _transaction.ExecuteAsync(async () =>
        {
            var patient = await _patients.GetByIdAsync(request.PatientId)
                ?? throw new ResourceNotFoundException("Bệnh nhân không tồn tại hoặc đã bị xóa.");
            var slot = await _repository.GetSlotAsync(request.AppointmentSlotId)
                ?? throw new ResourceNotFoundException("Khung giờ khám không tồn tại.");
            var unavailableReason = await GetUnavailableReasonAsync(slot);
            if (unavailableReason is not null)
                throw new AppointmentConflictException(unavailableReason);
            if (slot.Status != AppointmentSlotStatus.Available)
                throw new AppointmentConflictException("Khung giờ không còn trống. Vui lòng chọn giờ khác.");
            if (await _repository.HasDoctorOverlapAsync(slot.DoctorId, slot.Date, slot.StartTime, slot.EndTime))
                throw new AppointmentConflictException("Bác sĩ đã có lịch hẹn trong khoảng giờ này.");
            if (await _repository.HasPatientOverlapAsync(patient.PatientId, slot.Date, slot.StartTime, slot.EndTime))
                throw new AppointmentConflictException("Bệnh nhân đã có lịch hẹn trong khoảng giờ này.");

            // Kiểm tra lại thời gian ngay trước khi ghi, phòng trường hợp truy vấn mất lâu.
            if (slot.Date.ToDateTime(slot.StartTime) <= ClinicClock.Now)
                throw new AppointmentConflictException("Đã đến giờ bắt đầu khám, không thể đặt khung giờ này.");
            if (!await _repository.TryBookSlotAsync(slot.AppointmentSlotId))
                throw new AppointmentConflictException("Khung giờ vừa được đặt. Vui lòng chọn giờ khác.");

            var appointment = new Appointment
            {
                PatientId = patient.PatientId,
                PatientFullName = patient.FullName,
                PatientPhoneNumber = patient.PhoneNumber,
                AppointmentSlotId = slot.AppointmentSlotId,
                DoctorId = slot.DoctorId,
                DoctorFullName = slot.Doctor.FullName,
                SpecialtyId = slot.Doctor.SpecialtyId,
                SpecialtyName = slot.Doctor.Specialty.Name,
                Date = slot.Date, StartTime = slot.StartTime, EndTime = slot.EndTime,
                Reason = Normalize(request.Reason),
                Status = AppointmentStatus.Confirmed,
                CreatedAt = DateTime.UtcNow
            };
            await _repository.AddAsync(appointment);
            await RecordHistoryAsync(appointment, "Created", null);
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
            // Gửi lại cùng khung giờ không được nhả slot đang giữ.
            if (appointment.AppointmentSlotId == request.AppointmentSlotId)
                return MapToDto(appointment);

            _ = await _patients.GetByIdAsync(appointment.PatientId)
                ?? throw new ResourceNotFoundException("Bệnh nhân không tồn tại hoặc đã bị xóa.");
            var slot = await _repository.GetSlotAsync(request.AppointmentSlotId)
                ?? throw new ResourceNotFoundException("Khung giờ khám không tồn tại.");
            var unavailableReason = await GetUnavailableReasonAsync(slot);
            if (unavailableReason is not null)
                throw new AppointmentConflictException(unavailableReason);
            if (slot.Status != AppointmentSlotStatus.Available)
                throw new AppointmentConflictException("Khung giờ không còn trống. Vui lòng chọn giờ khác.");
            if (await _repository.HasDoctorOverlapAsync(slot.DoctorId, slot.Date, slot.StartTime, slot.EndTime, id))
                throw new AppointmentConflictException("Bác sĩ đã có lịch hẹn trong khoảng giờ này.");
            if (await _repository.HasPatientOverlapAsync(appointment.PatientId, slot.Date, slot.StartTime, slot.EndTime, id))
                throw new AppointmentConflictException("Bệnh nhân đã có lịch hẹn trong khoảng giờ này.");

            EnsureRescheduleDeadline(appointment);
            if (slot.Date.ToDateTime(slot.StartTime) <= ClinicClock.Now)
                throw new AppointmentConflictException("Đã đến giờ bắt đầu khám, không thể đổi sang khung giờ này.");
            if (!await _repository.TryBookSlotAsync(slot.AppointmentSlotId))
                throw new AppointmentConflictException("Khung giờ vừa được đặt. Vui lòng chọn giờ khác.");

            var oldSlotId = appointment.AppointmentSlotId;
            var before = Snapshot(appointment);
            appointment.AppointmentSlotId = slot.AppointmentSlotId;
            appointment.DoctorId = slot.DoctorId;
            appointment.DoctorFullName = slot.Doctor.FullName;
            appointment.SpecialtyId = slot.Doctor.SpecialtyId;
            appointment.SpecialtyName = slot.Doctor.Specialty.Name;
            appointment.Date = slot.Date;
            appointment.StartTime = slot.StartTime;
            appointment.EndTime = slot.EndTime;
            appointment.UpdatedAt = DateTime.UtcNow;
            await RecordHistoryAsync(appointment, "Rescheduled", before);
            await _repository.SaveChangesAsync();

            var oldSlot = await _repository.GetSlotAsync(oldSlotId)
                ?? throw new ResourceNotFoundException("Khung giờ khám cũ không tồn tại.");
            var reason = await GetUnavailableReasonAsync(oldSlot);
            if (reason is null && await _repository.HasDoctorOverlapAsync(
                oldSlot.DoctorId, oldSlot.Date, oldSlot.StartTime, oldSlot.EndTime))
                reason = "Bác sĩ có lịch hẹn khác trong khoảng giờ này.";
            await _repository.ReleaseSlotAsync(oldSlotId, reason);
            return MapToDto(appointment);
        }, cancellationToken);
    }

    private static void EnsureRescheduleDeadline(Appointment appointment)
    {
        if (appointment.Date.ToDateTime(appointment.StartTime) < ClinicClock.Now.AddHours(2))
            throw new AppointmentConflictException("Chỉ được đổi lịch trước giờ khám ít nhất 2 tiếng.");
    }

    public async Task<AppointmentDto> CancelAsync(int id, CancelAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        await _cancelValidator.ValidateAndThrowAsync(request, cancellationToken);
        return await _transaction.ExecuteAsync(async () =>
        {
            var appointment = await GetRequiredTrackingAsync(id);
            EnsureCanManage(appointment);
            // Retry hủy lịch cũ không được mở slot đã có người khác đặt lại.
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
            appointment.CancellationReason = Normalize(request.Reason);
            await RecordHistoryAsync(appointment, "Cancelled", before);
            await _repository.SaveChangesAsync();

            var slot = await _repository.GetSlotAsync(appointment.AppointmentSlotId)
                ?? throw new ResourceNotFoundException("Khung giờ khám không tồn tại.");
            var reason = await GetUnavailableReasonAsync(slot);
            if (reason is null && await _repository.HasDoctorOverlapAsync(
                slot.DoctorId, slot.Date, slot.StartTime, slot.EndTime))
                reason = "Bác sĩ có lịch hẹn khác trong khoảng giờ này.";
            await _repository.ReleaseSlotAsync(slot.AppointmentSlotId, reason);
            return MapToDto(appointment);
        }, cancellationToken);
    }

    public Task<AppointmentDto> CompleteAsync(int id, CancellationToken cancellationToken = default)
        => FinishAsync(id, AppointmentStatus.Completed, cancellationToken);

    public Task<AppointmentDto> MarkNoShowAsync(int id, CancellationToken cancellationToken = default)
        => FinishAsync(id, AppointmentStatus.NoShow, cancellationToken);

    private Task<AppointmentDto> FinishAsync(int id, AppointmentStatus status, CancellationToken cancellationToken)
        => _transaction.ExecuteAsync(async () =>
        {
            var appointment = await GetRequiredTrackingAsync(id);
            if (!IsStaff && (_currentUser.Role != AccountRole.Doctor || _currentUser.DoctorId != appointment.DoctorId))
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

    private async Task<Appointment> GetRequiredTrackingAsync(int id)
    {
        var appointment = await _repository.GetByIdAsync(id, tracking: true)
            ?? throw new ResourceNotFoundException("Lịch hẹn không tồn tại.");
        EnsureCanRead(appointment);
        return appointment;
    }

    public Task<AppointmentPageDto> GetMineAsync(AppointmentQuery query)
    {
        if (_currentUser.Role is not (AccountRole.Patient or AccountRole.Doctor))
            throw new AccessDeniedException();
        return GetAllAsync(query);
    }

    public async Task<List<AppointmentHistoryDto>> GetHistoryAsync(int id, int page = 1, int pageSize = 50)
    {
        var appointment = await _repository.GetByIdAsync(id)
            ?? throw new ResourceNotFoundException("Lịch hẹn không tồn tại.");
        EnsureCanRead(appointment);
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100)
            throw new ValidationException("Page phải từ 1 đến 1000000; PageSize từ 1 đến 100.");
        return (await _repository.GetHistoryAsync(id, page, pageSize)).Select(x => new AppointmentHistoryDto(
            x.AppointmentHistoryId, x.AppointmentId, x.ActorAccountId, x.ActorDisplayName, x.ActorRole,
            x.Action, x.BeforeJson is null ? null : JsonSerializer.Deserialize<AppointmentSnapshot>(x.BeforeJson),
            JsonSerializer.Deserialize<AppointmentSnapshot>(x.AfterJson)!, x.OccurredAt)).ToList();
    }

    private bool IsStaff => _currentUser.Role is AccountRole.Admin or AccountRole.Receptionist;

    private void ApplyAccessScope(AppointmentQuery query)
    {
        if (IsStaff) return;
        if (_currentUser.Role == AccountRole.Patient && _currentUser.PatientId.HasValue)
        {
            if (query.PatientId.HasValue && query.PatientId != _currentUser.PatientId) throw new AccessDeniedException();
            query.PatientId = _currentUser.PatientId;
        }
        else if (_currentUser.Role == AccountRole.Doctor && _currentUser.DoctorId.HasValue)
        {
            if (query.DoctorId.HasValue && query.DoctorId != _currentUser.DoctorId) throw new AccessDeniedException();
            query.DoctorId = _currentUser.DoctorId;
        }
        else throw new AccessDeniedException();
    }

    private void EnsureCanRead(Appointment appointment)
    {
        if (IsStaff || (_currentUser.Role == AccountRole.Patient && _currentUser.PatientId == appointment.PatientId) ||
            (_currentUser.Role == AccountRole.Doctor && _currentUser.DoctorId == appointment.DoctorId)) return;
        // Không tiết lộ sự tồn tại của lịch người khác qua ID.
        throw new ResourceNotFoundException("Lịch hẹn không tồn tại.");
    }

    private void EnsureCanManage(Appointment appointment)
    {
        if (!IsStaff && (_currentUser.Role != AccountRole.Patient || _currentUser.PatientId != appointment.PatientId))
            throw new AccessDeniedException();
    }

    private Task RecordHistoryAsync(Appointment appointment, string action, AppointmentSnapshot? before)
        => _repository.AddHistoryAsync(new AppointmentHistory
        {
            Appointment = appointment, ActorAccountId = _currentUser.AccountId,
            ActorDisplayName = _currentUser.DisplayName, ActorRole = _currentUser.Role.ToString(), Action = action,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = JsonSerializer.Serialize(Snapshot(appointment)), OccurredAt = DateTime.UtcNow
        });

    private static AppointmentSnapshot Snapshot(Appointment x) => new(x.AppointmentSlotId, x.DoctorId,
        x.DoctorFullName, x.SpecialtyId, x.SpecialtyName, x.Date, x.StartTime, x.EndTime, x.Status, x.CancellationReason);

    private async Task<string?> GetUnavailableReasonAsync(AppointmentSlot slot)
    {
        if (slot.Doctor.IsDelete)
            return "Bác sĩ không còn nhận lịch khám.";
        if (!string.Equals(slot.Doctor.Specialty.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return "Chuyên khoa không còn hoạt động.";

        var now = ClinicClock.Now;
        var today = DateOnly.FromDateTime(now);
        if (slot.Date < today || slot.Date > today.AddDays(6) ||
            slot.Date.ToDateTime(slot.StartTime) <= now)
            return "Khung giờ phải chưa bắt đầu và nằm trong 7 ngày từ hôm nay.";

        var assignment = slot.DoctorShift;
        var shift = assignment.Shift;
        if (assignment.IsDeleted || assignment.DoctorId != slot.DoctorId ||
            shift.IsDeleted || !shift.IsActive || shift.DayOfWeek != slot.Date.DayOfWeek ||
            slot.StartTime < shift.StartTime || slot.EndTime > shift.EndTime ||
            (slot.EndTime - slot.StartTime).TotalMinutes != 30 ||
            (slot.StartTime - shift.StartTime).TotalMinutes % 30 != 0)
            return "Khung giờ không còn thuộc lịch làm việc hiện tại.";

        if (await _nonWorkingDays.IsDoctorUnavailableAsync(slot.DoctorId, slot.Date))
            return "Bác sĩ hoặc bệnh viện nghỉ vào ngày này.";
        return null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
