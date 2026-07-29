using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories
{
    public interface IDoctorRepository
    {
        Task<List<DoctorDto>> GetListDoctorAsync();
        Task<DoctorDto?> GetDoctorByIdAsync(int doctorId);
        Task<Doctor?> GetDoctorByIdTrackingAsync(int doctorId);
        Task AddAsync(Doctor doctor);
        void Update(Doctor doctor);
        void Remove(Doctor doctor);
        Task SaveChangesAsync();

    }
}
