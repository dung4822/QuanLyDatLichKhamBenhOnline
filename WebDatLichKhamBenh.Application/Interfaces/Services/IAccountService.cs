using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.DTOs.Patients;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IAccountService
{
    Task<MyProfileDto> GetMyProfileAsync();
    Task<PatientDto> UpdateMyPatientProfileAsync(UpdatePatientRequest request);
    Task<AccountDto> CreateAsync(CreateAccountRequest request);
    Task<List<AccountDto>> GetPageAsync(int page, int pageSize);
    Task<AccountDto> UpdateAccessAsync(Guid id, UpdateAccountAccessRequest request);
    // Chỉ được gọi từ lệnh khởi tạo quản trị viên tại máy chủ, không có HTTP endpoint.
    Task BootstrapAdminAsync(string email, string password);
}
