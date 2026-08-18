using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Shifts;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

public class ShiftService : IShiftService
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IAppointmentSlotRepository _appointmentSlotRepository;
    private readonly IAppointmentSlotService _appointmentSlotService;
    private readonly IValidator<CreateShiftRequest> _createValidator;
    private readonly IValidator<UpdateShiftRequest> _updateValidator;

    public ShiftService(
        IShiftRepository shiftRepository,
        IAppointmentSlotRepository appointmentSlotRepository,
        IAppointmentSlotService appointmentSlotService,
        IValidator<CreateShiftRequest> createValidator,
        IValidator<UpdateShiftRequest> updateValidator)
    {
        _shiftRepository = shiftRepository;
        _appointmentSlotRepository = appointmentSlotRepository;
        _appointmentSlotService = appointmentSlotService;
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

    public async Task<ShiftDto> CreateAsync(CreateShiftRequest createShiftRequest)
    {
        await _createValidator.ValidateAndThrowAsync(createShiftRequest);

        var shift = new Shift
        {
            DayOfWeek = createShiftRequest.DayOfWeek,
            Name = createShiftRequest.Name.Trim(),
            StartTime = createShiftRequest.StartTime,
            EndTime = createShiftRequest.EndTime,
            IsActive = createShiftRequest.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _shiftRepository.AddAsync(shift);
            await _shiftRepository.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            throw new DataPersistenceException("Không thể lưu ca làm việc vào database.", exception);
        }

        return MapToDto(shift);
    }

    public async Task<ShiftDto?> UpdateAsync(int shiftId, UpdateShiftRequest updateShiftRequest)
    {
        await _updateValidator.ValidateAndThrowAsync(updateShiftRequest);

        var shift = await _shiftRepository.GetByIdTrackingAsync(shiftId);
        if (shift is null)
        {
            return null;
        }

        await EnsureNoOverlappingAssignmentsAsync(shiftId, updateShiftRequest);

        shift.DayOfWeek = updateShiftRequest.DayOfWeek;
        shift.Name = updateShiftRequest.Name.Trim();
        shift.StartTime = updateShiftRequest.StartTime;
        shift.EndTime = updateShiftRequest.EndTime;
        shift.IsActive = updateShiftRequest.IsActive;
        shift.UpdatedAt = DateTime.UtcNow;

        await SaveChangesAsync("Không thể cập nhật ca làm việc trong database.");
        await _appointmentSlotService.EnsureRollingWindowAsync();
        return MapToDto(shift);
    }

    public async Task<bool> DeleteAsync(int shiftId)
    {
        var shift = await _shiftRepository.GetByIdTrackingAsync(shiftId);
        if (shift is null)
        {
            return false;
        }

        var deletedAt = DateTime.UtcNow;
        shift.IsDeleted = true;
        shift.DeletedAt = deletedAt;
        shift.UpdatedAt = deletedAt;

        await SaveChangesAsync("Không thể xóa mềm ca làm việc trong database.");
        await _appointmentSlotService.EnsureRollingWindowAsync();
        return true;
    }

    private async Task SaveChangesAsync(string errorMessage)
    {
        try
        {
            await _shiftRepository.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            throw new DataPersistenceException(errorMessage, exception);
        }
    }

    private async Task EnsureNoOverlappingAssignmentsAsync(int shiftId, UpdateShiftRequest updateShiftRequest)
    {
        if (!updateShiftRequest.IsActive)
        {
            return;
        }

        var affectedAssignments = await _appointmentSlotRepository.GetDoctorShiftsByShiftIdAsync(shiftId);

        if (affectedAssignments.Count == 0)
        {
            return;
        }

        var allAssignments = await _appointmentSlotRepository.GetActiveDoctorShiftsAsync();
        foreach (var assignment in affectedAssignments)
        {
            var overlaps = allAssignments.Any(other =>
                other.DoctorId == assignment.DoctorId &&
                other.ShiftId != shiftId &&
                other.Shift.DayOfWeek == updateShiftRequest.DayOfWeek &&
                updateShiftRequest.StartTime < other.Shift.EndTime &&
                other.Shift.StartTime < updateShiftRequest.EndTime);

            if (overlaps)
            {
                throw new ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(UpdateShiftRequest.StartTime),
                        "Ca làm việc mới bị chồng giờ với lịch hiện có của ít nhất một bác sĩ.")
                });
            }
        }
    }

    private static ShiftDto MapToDto(Shift shift)
    {
        return new ShiftDto
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
}
