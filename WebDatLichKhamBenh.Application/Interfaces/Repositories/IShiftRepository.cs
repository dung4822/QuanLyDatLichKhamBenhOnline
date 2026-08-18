using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface IShiftRepository
{
    Task<List<Shift>> GetAllAsync();
    Task<List<Shift>> GetByIdsAsync(IReadOnlyCollection<int> shiftIds);
    Task<Shift?> GetByIdAsync(int shiftId);
    Task<Shift?> GetByIdTrackingAsync(int shiftId);
    Task AddAsync(Shift shift);
    Task SaveChangesAsync();
}
