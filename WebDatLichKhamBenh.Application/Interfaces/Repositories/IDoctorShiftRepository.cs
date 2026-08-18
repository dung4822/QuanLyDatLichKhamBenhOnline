using WebDatLichKhamBenh.Application.DTOs.DoctorShifts;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface IDoctorShiftRepository
{
    Task<List<DoctorShiftDto>> GetByDoctorIdAsync(int doctorId);
    Task<List<DoctorShift>> GetByDoctorIdTrackingAsync(int doctorId);
    Task AddAsync(DoctorShift doctorShift);
    Task SaveChangesAsync();
}
