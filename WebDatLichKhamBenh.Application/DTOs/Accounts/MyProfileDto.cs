using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Application.DTOs.Patients;

namespace WebDatLichKhamBenh.Application.DTOs.Accounts;

public record MyProfileDto(AccountDto Account, PatientDto? Patient, DoctorDto? Doctor);
