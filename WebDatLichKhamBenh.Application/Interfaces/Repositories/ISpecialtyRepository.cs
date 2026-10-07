using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface ISpecialtyRepository
{
    Task<List<Specialty>> GetAllAsync();
    Task<Specialty?> GetByIdAsync(int specialtyId);
    Task<Specialty?> GetByIdTrackingAsync(int specialtyId);
    Task AddAsync(Specialty specialty);
    Task SaveChangesAsync();
}
