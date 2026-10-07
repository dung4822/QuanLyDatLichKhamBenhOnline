using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Specialties;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Services;

public class SpecialtyService : ISpecialtyService
{
    private readonly ISpecialtyRepository _specialtyRepository;
    private readonly IAppointmentRepository _appointments;
    private readonly IAppointmentSlotService _slots;
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IValidator<CreateSpecialtyRequest> _createValidator;
    private readonly IValidator<UpdateSpecialtyRequest> _updateValidator;

    public SpecialtyService(
        ISpecialtyRepository specialtyRepository,
        IAppointmentRepository appointments,
        IAppointmentSlotService slots,
        IClinicScheduleTransaction transaction,
        IValidator<CreateSpecialtyRequest> createValidator,
        IValidator<UpdateSpecialtyRequest> updateValidator)
    {
        _specialtyRepository = specialtyRepository;
        _appointments = appointments;
        _slots = slots;
        _transaction = transaction;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<List<SpecialtyDto>> GetAllAsync()
    {
        var specialties = await _specialtyRepository.GetAllAsync();
        return specialties.Select(MapToDto).ToList();
    }

    public async Task<SpecialtyDto?> GetByIdAsync(int specialtyId)
    {
        var specialty = await _specialtyRepository.GetByIdAsync(specialtyId);
        return specialty is null ? null : MapToDto(specialty);
    }

    public async Task<SpecialtyDto> CreateAsync(CreateSpecialtyRequest request)
    {
        await _createValidator.ValidateAndThrowAsync(request);

        var specialty = new Specialty
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Status = request.Status.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        await _specialtyRepository.AddAsync(specialty);
        await _specialtyRepository.SaveChangesAsync();
        return MapToDto(specialty);
    }

    public async Task<SpecialtyDto?> UpdateAsync(int specialtyId, UpdateSpecialtyRequest request)
    {
        await _updateValidator.ValidateAndThrowAsync(request);

        return await _transaction.ExecuteAsync(async () =>
        {
            var specialty = await _specialtyRepository.GetByIdTrackingAsync(specialtyId);
            if (specialty is null)
                return null;

            // Không có gì thay đổi thì khỏi ghi DB và khỏi đồng bộ lại khung giờ.
            if (request.Name == specialty.Name && request.Description == specialty.Description &&
                request.Status == specialty.Status)
                return MapToDto(specialty);

            if (!SpecialtyStatus.IsActive(request.Status))
                await EnsureNoUpcomingAppointmentsAsync(specialtyId);

            specialty.Name = request.Name.Trim();
            specialty.Description = request.Description?.Trim();
            specialty.Status = request.Status.Trim();
            specialty.UpdatedAt = DateTime.UtcNow;
            await _specialtyRepository.SaveChangesAsync();

            // Đổi trạng thái chuyên khoa ảnh hưởng tới khung giờ còn trống của các bác sĩ thuộc khoa.
            await _slots.EnsureRollingWindowAsync();
            return MapToDto(specialty);
        });
    }

    // "Xóa" chuyên khoa = chuyển sang Inactive, để giữ lịch sử lịch hẹn cũ.
    public Task<bool> DeleteAsync(int specialtyId)
        => _transaction.ExecuteAsync(async () =>
        {
            var specialty = await _specialtyRepository.GetByIdTrackingAsync(specialtyId);
            if (specialty is null)
                return false;

            await EnsureNoUpcomingAppointmentsAsync(specialtyId);
            specialty.Status = SpecialtyStatus.Inactive;
            specialty.UpdatedAt = DateTime.UtcNow;
            await _specialtyRepository.SaveChangesAsync();
            await _slots.EnsureRollingWindowAsync();
            return true;
        });

    private async Task EnsureNoUpcomingAppointmentsAsync(int specialtyId)
    {
        if (await _appointments.HasUpcomingConfirmedAsync(ClinicClock.Now, specialtyId: specialtyId))
            throw new AppointmentConflictException("Chuyên khoa còn lịch hẹn chưa kết thúc, không thể ngừng hoạt động.");
    }

    private static SpecialtyDto MapToDto(Specialty specialty) => new()
    {
        SpecialtyId = specialty.SpecialtyId,
        Name = specialty.Name,
        Description = specialty.Description,
        Status = specialty.Status,
        CreatedAt = specialty.CreatedAt,
        UpdatedAt = specialty.UpdatedAt
    };
}
