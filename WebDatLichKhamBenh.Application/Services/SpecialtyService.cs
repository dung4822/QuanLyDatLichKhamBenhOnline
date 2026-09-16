using WebDatLichKhamBenh.Application.DTOs.Specialties;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Time;

namespace WebDatLichKhamBenh.Application.Services;

public class SpecialtyService : ISpecialtyService
{
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IAppointmentRepository _appointments;
    private readonly IAppointmentSlotService _slots;
    private readonly ISpecialtyRepository _specialtyRepository;

    public SpecialtyService(ISpecialtyRepository specialtyRepository, IClinicScheduleTransaction transaction,
        IAppointmentRepository appointments, IAppointmentSlotService slots)
    {
        _transaction = transaction;
        _appointments = appointments;
        _slots = slots;
        _specialtyRepository = specialtyRepository;
    }

    public async Task<List<SpecialtyDto>> GetAllAsync()
    {
        var specialties = await _specialtyRepository.GetAllAsync();
        return specialties.Select(MapToDto).ToList();
    }

#if false
    public async Task<SpecialtyDto?> GetByIdAsync(int specialtyId)
    {
        var specialty = await _specialtyRepository.GetByIdAsync(specialtyId);
        return specialty == null ? null : MapToDto(specialty);
    }
#endif

    public async Task<SpecialtyDto> CreateAsync(CreateSpecialtyRequest createSpecialtyRequest)
    {
        var specialty = new Specialty
        {
            Name = createSpecialtyRequest.Name.Trim(),
            Description = createSpecialtyRequest.Description?.Trim(),
            Status = createSpecialtyRequest.Status.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _specialtyRepository.AddAsync(specialty);
        await _specialtyRepository.SaveChangesAsync();

        return MapToDto(specialty);
    }

    public Task<SpecialtyDto?> UpdateAsync(int specialtyId, UpdateSpecialtyRequest request)
        => _transaction.ExecuteAsync(() => UpdateCoreAsync(specialtyId, request));

    private async Task<SpecialtyDto?> UpdateCoreAsync(int specialtyId, UpdateSpecialtyRequest updateSpecialtyRequest)
    {
        var specialty = await _specialtyRepository.GetByIdTrackingAsync(specialtyId);
        if (specialty == null)
        {
            return null;
        }
        if(updateSpecialtyRequest.Name == specialty.Name &&
            updateSpecialtyRequest.Description == specialty.Description &&
            updateSpecialtyRequest.Status == specialty.Status) return MapToDto(specialty);


        if (!string.Equals(updateSpecialtyRequest.Status.Trim(), "Active", StringComparison.OrdinalIgnoreCase))
            await EnsureNoAppointmentsAsync(specialtyId);
        specialty.Name = updateSpecialtyRequest.Name.Trim();
        specialty.Description = updateSpecialtyRequest.Description?.Trim();
        specialty.Status = updateSpecialtyRequest.Status.Trim();
        specialty.UpdatedAt = DateTime.UtcNow;

        await _specialtyRepository.SaveChangesAsync();
        await _slots.EnsureRollingWindowAsync();
        return MapToDto(specialty);
    }


    public Task<bool> DeleteAsync(int specialtyId)
        => _transaction.ExecuteAsync(() => DeleteCoreAsync(specialtyId));

    private async Task<bool> DeleteCoreAsync(int specialtyId)
    {
        var specialty = await _specialtyRepository.GetByIdAsync(specialtyId);
        if (specialty == null)
        {
            return false;
        }

        await EnsureNoAppointmentsAsync(specialtyId);
        specialty.Status = "Inactive";
        specialty.UpdatedAt = DateTime.UtcNow;

        _specialtyRepository.Update(specialty);
        await _specialtyRepository.SaveChangesAsync();
        await _slots.EnsureRollingWindowAsync();

        return true;
    }

    private async Task EnsureNoAppointmentsAsync(int specialtyId)
    {
        if (await _appointments.HasUpcomingConfirmedAsync(ClinicClock.Now, specialtyId: specialtyId))
            throw new AppointmentConflictException("Chuyên khoa còn lịch hẹn chưa kết thúc, không thể ngừng hoạt động.");
    }

    private static SpecialtyDto MapToDto(Specialty specialty)
    {
        return new SpecialtyDto
        {
            SpecialtyId = specialty.SpecialtyId,
            Name = specialty.Name,
            Description = specialty.Description,
            Status = specialty.Status,
            CreatedAt = specialty.CreatedAt,
            UpdatedAt = specialty.UpdatedAt
        };
    }

    public async Task<SpecialtyDto?> GetByIdAsync(int specialtyId)
    {
        var specialty = await _specialtyRepository.GetByIdAsync(specialtyId);
        return specialty == null ? null : MapToDto(specialty);
    }
}
