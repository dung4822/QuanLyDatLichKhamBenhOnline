using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface INonWorkingDayRepository
{
    Task<List<NonWorkingDay>> GetAllAsync(DateOnly? fromDate);
    Task<NonWorkingDay?> GetByIdAsync(int nonWorkingDayId);
    Task<NonWorkingDay?> GetByIdTrackingAsync(int nonWorkingDayId);
    Task<bool> ExistsAsync(DateOnly date, int? doctorId, int? excludedNonWorkingDayId = null);
    Task<bool> IsDoctorUnavailableAsync(int doctorId, DateOnly date);
    Task AddAsync(NonWorkingDay nonWorkingDay);
    Task SaveChangesAsync();
}
