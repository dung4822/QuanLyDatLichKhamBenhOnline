using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface IDoctorRepository
{
    Task<List<DoctorDto>> GetListDoctorAsync(int? specialtyId = null);
    Task<DoctorDto?> GetDoctorByIdAsync(int doctorId);
    Task<Doctor?> GetDoctorByIdTrackingAsync(int doctorId);
    Task AddAsync(Doctor doctor);
    Task SaveChangesAsync();
}
