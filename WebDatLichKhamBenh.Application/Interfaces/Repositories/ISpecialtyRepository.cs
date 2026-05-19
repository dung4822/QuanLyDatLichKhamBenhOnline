using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface ISpecialtyRepository
{
    Task<List<Specialty>> GetAllAsync();
    Task<Specialty?> GetByIdAsync(int specialtyId);
    Task AddAsync(Specialty specialty);
    void Update(Specialty specialty);
    void Remove(Specialty specialty);
    Task SaveChangesAsync();
}
