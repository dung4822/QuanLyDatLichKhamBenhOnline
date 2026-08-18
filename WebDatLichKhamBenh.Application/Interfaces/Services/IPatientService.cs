using WebDatLichKhamBenh.Application.DTOs.Patients;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IPatientService
{
    Task<List<PatientDto>> GetAllAsync();
    Task<PatientDto?> GetByIdAsync(int patientId);
    Task<PatientDto> CreateAsync(CreatePatientRequest createPatientRequest);
    Task<PatientDto?> UpdateAsync(int patientId, UpdatePatientRequest updatePatientRequest);
    Task<bool> DeleteAsync(int patientId);
}
