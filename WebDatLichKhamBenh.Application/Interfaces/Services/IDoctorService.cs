using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebDatLichKhamBenh.Application.DTOs.Doctors;

namespace WebDatLichKhamBenh.Application.Interfaces.Services
{
    public interface IDoctorService
    {
        Task<List<DoctorDto>> GetAllAsync();
        Task<DoctorDto?> GetByIdAsync(int doctorId);
        Task<DoctorDto> CreateAsync(
            CreateDoctorDto createDoctorDto,
            CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int doctorId);
        Task<DoctorDto?> UpdateAsync(
            int doctorId,
            UpdateDoctorDto updateDoctorDto,
            CancellationToken cancellationToken = default);
    }
}
