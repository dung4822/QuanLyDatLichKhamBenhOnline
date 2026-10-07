using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Shifts;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

public class ShiftService : IShiftService
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IAppointmentRepository _appointments;
    private readonly IAppointmentSlotRepository _appointmentSlotRepository;
    private readonly IAppointmentSlotService _appointmentSlotService;
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IValidator<CreateShiftRequest> _createValidator;
    private readonly IValidator<UpdateShiftRequest> _updateValidator;

    public ShiftService(
        IShiftRepository shiftRepository,
        IAppointmentRepository appointments,
        IAppointmentSlotRepository appointmentSlotRepository,
        IAppointmentSlotService appointmentSlotService,
        IClinicScheduleTransaction transaction,
        IValidator<CreateShiftRequest> createValidator,
        IValidator<UpdateShiftRequest> updateValidator)
    {
        _shiftRepository = shiftRepository;
        _appointments = appointments;
        _appointmentSlotRepository = appointmentSlotRepository;
        _appointmentSlotService = appointmentSlotService;
        _transaction = transaction;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<List<ShiftDto>> GetAllAsync()
    {
        var shifts = await _shiftRepository.GetAllAsync();
        return shifts.Select(MapToDto).ToList();
    }

    public async Task<ShiftDto?> GetByIdAsync(int shiftId)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId);
        return shift is null ? null : MapToDto(shift);
    }

    // Ca mới chưa được phân công cho bác sĩ nào nên chưa ảnh hưởng khung giờ; không cần transaction.
    public async Task<ShiftDto> CreateAsync(CreateShiftRequest request)
    {
        await _createValidator.ValidateAndThrowAsync(request);

        var shift = new Shift
        {
            DayOfWeek = request.DayOfWeek,
            Name = request.Name.Trim(),
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        await _shiftRepository.AddAsync(shift);
        await _shiftRepository.SaveChangesAsync();
        return MapToDto(shift);
    }

    public async Task<ShiftDto?> UpdateAsync(int shiftId, UpdateShiftRequest request)
    {
        await _updateValidator.ValidateAndThrowAsync(request);

        return await _transaction.ExecuteAsync(async () =>
        {
            var shift = await _shiftRepository.GetByIdTrackingAsync(shiftId);
            if (shift is null)
                return null;

            await EnsureNoOverlappingAssignmentsAsync(shiftId, request);

            var scheduleChanged = shift.DayOfWeek != request.DayOfWeek || shift.StartTime != request.StartTime ||
                shift.EndTime != request.EndTime || shift.IsActive != request.IsActive;
            if (scheduleChanged && await _appointments.HasUpcomingConfirmedAsync(ClinicClock.Now, shiftId: shiftId))
                throw new AppointmentConflictException("Ca này có lịch hẹn chưa kết thúc. Hãy xử lý lịch hẹn trước khi thay đổi ca.");

            shift.DayOfWeek = request.DayOfWeek;
            shift.Name = request.Name.Trim();
            shift.StartTime = request.StartTime;
            shift.EndTime = request.EndTime;
            shift.IsActive = request.IsActive;
            shift.UpdatedAt = DateTime.UtcNow;
            await _shiftRepository.SaveChangesAsync();

            await _appointmentSlotService.EnsureRollingWindowAsync();
            return MapToDto(shift);
        });
    }

    public Task<bool> DeleteAsync(int shiftId)
        => _transaction.ExecuteAsync(async () =>
        {
            var shift = await _shiftRepository.GetByIdTrackingAsync(shiftId);
            if (shift is null)
                return false;

            if (await _appointments.HasUpcomingConfirmedAsync(ClinicClock.Now, shiftId: shiftId))
                throw new AppointmentConflictException("Ca này có lịch hẹn chưa kết thúc, không thể xóa.");

            var deletedAt = DateTime.UtcNow;
            shift.IsDeleted = true;
            shift.DeletedAt = deletedAt;
            shift.UpdatedAt = deletedAt;
            await _shiftRepository.SaveChangesAsync();

            await _appointmentSlotService.EnsureRollingWindowAsync();
            return true;
        });

    // Đổi giờ ca có thể làm nó chồng lên ca khác của cùng một bác sĩ trong cùng ngày.
    private async Task EnsureNoOverlappingAssignmentsAsync(int shiftId, UpdateShiftRequest request)
    {
        if (!request.IsActive)
            return;

        var affectedAssignments = await _appointmentSlotRepository.GetDoctorShiftsByShiftIdAsync(shiftId);
        if (affectedAssignments.Count == 0)
            return;

        var allAssignments = await _appointmentSlotRepository.GetActiveDoctorShiftsAsync();
        var overlaps = affectedAssignments.Any(assignment => allAssignments.Any(other =>
            other.DoctorId == assignment.DoctorId &&
            other.ShiftId != shiftId &&
            other.Shift.DayOfWeek == request.DayOfWeek &&
            request.StartTime < other.Shift.EndTime &&
            other.Shift.StartTime < request.EndTime));

        if (overlaps)
            throw ValidationErrors.For(nameof(UpdateShiftRequest.StartTime),
                "Ca làm việc mới bị chồng giờ với lịch hiện có của ít nhất một bác sĩ.");
    }

    private static ShiftDto MapToDto(Shift shift) => new()
    {
        ShiftId = shift.ShiftId,
        DayOfWeek = shift.DayOfWeek,
        Name = shift.Name,
        StartTime = shift.StartTime,
        EndTime = shift.EndTime,
        IsActive = shift.IsActive,
        CreatedAt = shift.CreatedAt,
        UpdatedAt = shift.UpdatedAt
    };
}
