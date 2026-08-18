using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface IPatientRepository
{
    Task<List<Patient>> GetAllAsync();
    Task<Patient?> GetByIdAsync(int patientId);
    Task<Patient?> GetByIdTrackingAsync(int patientId);
    Task AddAsync(Patient patient);
    Task SaveChangesAsync();
}
