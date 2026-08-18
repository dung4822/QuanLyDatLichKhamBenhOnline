using FluentValidation;
using FluentValidation.Results;
using WebDatLichKhamBenh.Application.DTOs.NonWorkingDays;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

public class NonWorkingDayService : INonWorkingDayService
{
    private readonly INonWorkingDayRepository _nonWorkingDayRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IValidator<CreateNonWorkingDayRequest> _createValidator;
    private readonly IValidator<UpdateNonWorkingDayRequest> _updateValidator;
    private readonly IAppointmentSlotService _appointmentSlotService;

    public NonWorkingDayService(
        INonWorkingDayRepository nonWorkingDayRepository,
        IDoctorRepository doctorRepository,
        IValidator<CreateNonWorkingDayRequest> createValidator,
        IValidator<UpdateNonWorkingDayRequest> updateValidator,
        IAppointmentSlotService appointmentSlotService)
    {
        _nonWorkingDayRepository = nonWorkingDayRepository;
        _doctorRepository = doctorRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _appointmentSlotService = appointmentSlotService;
    }

    public async Task<List<NonWorkingDayDto>> GetAllAsync(DateOnly? fromDate)
    {
        var nonWorkingDays = await _nonWorkingDayRepository.GetAllAsync(fromDate);
        return nonWorkingDays.Select(MapToDto).ToList();
    }

    public async Task<NonWorkingDayDto?> GetByIdAsync(int nonWorkingDayId)
    {
        var nonWorkingDay = await _nonWorkingDayRepository.GetByIdAsync(nonWorkingDayId);
        return nonWorkingDay is null ? null : MapToDto(nonWorkingDay);
    }

    public async Task<NonWorkingDayDto> CreateAsync(CreateNonWorkingDayRequest createNonWorkingDayRequest)
    {
        await _createValidator.ValidateAndThrowAsync(createNonWorkingDayRequest);
        await EnsureDoctorExistsAsync(createNonWorkingDayRequest.DoctorId);
        await EnsureNoDuplicateAsync(createNonWorkingDayRequest.Date, createNonWorkingDayRequest.DoctorId);

        var nonWorkingDay = new NonWorkingDay
        {
            Date = createNonWorkingDayRequest.Date,
            DoctorId = createNonWorkingDayRequest.DoctorId,
            Reason = NormalizeReason(createNonWorkingDayRequest.Reason),
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _nonWorkingDayRepository.AddAsync(nonWorkingDay);
            await _nonWorkingDayRepository.SaveChangesAsync();
            await _appointmentSlotService.SyncRangeAsync(
                nonWorkingDay.Date,
                nonWorkingDay.Date,
                nonWorkingDay.DoctorId);
        }
        catch (Exception exception)
        {
            throw new DataPersistenceException("Không thể lưu lịch nghỉ vào database.", exception);
        }

        return await GetSavedNonWorkingDayAsync(nonWorkingDay.NonWorkingDayId);
    }

    public async Task<NonWorkingDayDto?> UpdateAsync(
        int nonWorkingDayId,
        UpdateNonWorkingDayRequest updateNonWorkingDayRequest)
    {
        await _updateValidator.ValidateAndThrowAsync(updateNonWorkingDayRequest);

        var nonWorkingDay = await _nonWorkingDayRepository.GetByIdTrackingAsync(nonWorkingDayId);
        if (nonWorkingDay is null)
        {
            return null;
        }

        EnsureStillFuture(nonWorkingDay.Date);
        var oldDate = nonWorkingDay.Date;
        var oldDoctorId = nonWorkingDay.DoctorId;
        await EnsureDoctorExistsAsync(updateNonWorkingDayRequest.DoctorId);
        await EnsureNoDuplicateAsync(
            updateNonWorkingDayRequest.Date,
            updateNonWorkingDayRequest.DoctorId,
            nonWorkingDayId);

        nonWorkingDay.Date = updateNonWorkingDayRequest.Date;
        nonWorkingDay.DoctorId = updateNonWorkingDayRequest.DoctorId;
        nonWorkingDay.Reason = NormalizeReason(updateNonWorkingDayRequest.Reason);
        nonWorkingDay.UpdatedAt = DateTime.UtcNow;

        await SaveChangesAsync("Không thể cập nhật lịch nghỉ trong database.");
        await _appointmentSlotService.SyncRangeAsync(oldDate, oldDate, oldDoctorId);
        await _appointmentSlotService.SyncRangeAsync(nonWorkingDay.Date, nonWorkingDay.Date, nonWorkingDay.DoctorId);
        return await GetSavedNonWorkingDayAsync(nonWorkingDayId);
    }

    public async Task<bool> DeleteAsync(int nonWorkingDayId)
    {
        var nonWorkingDay = await _nonWorkingDayRepository.GetByIdTrackingAsync(nonWorkingDayId);
        if (nonWorkingDay is null)
        {
            return false;
        }

        EnsureStillFuture(nonWorkingDay.Date);
        var affectedDate = nonWorkingDay.Date;
        var affectedDoctorId = nonWorkingDay.DoctorId;

        var deletedAt = DateTime.UtcNow;
        nonWorkingDay.IsDeleted = true;
        nonWorkingDay.DeletedAt = deletedAt;
        nonWorkingDay.UpdatedAt = deletedAt;

        await SaveChangesAsync("Không thể xóa mềm lịch nghỉ trong database.");
        await _appointmentSlotService.SyncRangeAsync(affectedDate, affectedDate, affectedDoctorId);
        return true;
    }

    public Task<bool> IsDoctorUnavailableAsync(int doctorId, DateOnly date)
    {
        return _nonWorkingDayRepository.IsDoctorUnavailableAsync(doctorId, date);
    }

    private async Task EnsureDoctorExistsAsync(int? doctorId)
    {
        if (!doctorId.HasValue)
        {
            return;
        }

        var doctor = await _doctorRepository.GetDoctorByIdTrackingAsync(doctorId.Value);
        if (doctor is null)
        {
            throw CreateValidationException(nameof(CreateNonWorkingDayRequest.DoctorId), "Bác sĩ không tồn tại hoặc đã bị xóa.");
        }
    }

    private async Task EnsureNoDuplicateAsync(DateOnly date, int? doctorId, int? excludedNonWorkingDayId = null)
    {
        var hasDuplicate = await _nonWorkingDayRepository.ExistsAsync(date, doctorId, excludedNonWorkingDayId);
        if (hasDuplicate)
        {
            var scope = doctorId.HasValue ? "bác sĩ này" : "cả bệnh viện";
            throw CreateValidationException(nameof(CreateNonWorkingDayRequest.Date), $"Đã có lịch nghỉ cho {scope} vào ngày này.");
        }
    }

    private static void EnsureStillFuture(DateOnly date)
    {
        if (date <= ClinicClock.Today)
        {
            throw CreateValidationException(nameof(CreateNonWorkingDayRequest.Date), "Chỉ có thể sửa hoặc hủy lịch nghỉ của ngày trong tương lai.");
        }
    }

    private async Task<NonWorkingDayDto> GetSavedNonWorkingDayAsync(int nonWorkingDayId)
    {
        return await GetByIdAsync(nonWorkingDayId)
            ?? throw new InvalidOperationException("Không thể đọc lịch nghỉ vừa được lưu.");
    }

    private async Task SaveChangesAsync(string errorMessage)
    {
        try
        {
            await _nonWorkingDayRepository.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            throw new DataPersistenceException(errorMessage, exception);
        }
    }

    private static string? NormalizeReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private static NonWorkingDayDto MapToDto(NonWorkingDay nonWorkingDay)
    {
        return new NonWorkingDayDto
        {
            NonWorkingDayId = nonWorkingDay.NonWorkingDayId,
            Date = nonWorkingDay.Date,
            DoctorId = nonWorkingDay.DoctorId,
            DoctorFullName = nonWorkingDay.Doctor?.FullName,
            Reason = nonWorkingDay.Reason,
            CreatedAt = nonWorkingDay.CreatedAt,
            UpdatedAt = nonWorkingDay.UpdatedAt
        };
    }

    private static ValidationException CreateValidationException(string propertyName, string errorMessage)
    {
        return new ValidationException(new[]
        {
            new ValidationFailure(propertyName, errorMessage)
        });
    }
}
