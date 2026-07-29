using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface ISpecialtyRepository
{
    Task<List<Specialty>> GetAllAsync();
#if false
    Task<Specialty?> GetByIdAsync(int specialtyId);
#endif
    Task<Specialty?> GetByIdAsync(int specialtyId);
    Task<Specialty?> GetByIdTrackingAsync(int specialtyId);
    Task AddAsync(Specialty specialty);
    void Update(Specialty specialty);
    void Remove(Specialty specialty);
    Task<bool> ExistsAsync(int ispecialtyId);


    Task SaveChangesAsync();
}
