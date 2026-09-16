using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.DTOs.Patients;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IAccountService
{
    Task<AccountDto> RegisterAsync(RegisterRequest request);
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task<UserSession?> AuthenticateAsync(string token);
    Task LogoutAsync();
    Task ChangePasswordAsync(ChangePasswordRequest request);
    Task<MyProfileDto> GetMyProfileAsync();
    Task<PatientDto> UpdateMyPatientProfileAsync(UpdatePatientRequest request);
    Task<AccountDto> CreateAsync(CreateAccountRequest request);
    Task<List<AccountDto>> GetPageAsync(int page, int pageSize);
    Task<AccountDto> UpdateAccessAsync(Guid id, UpdateAccountAccessRequest request);
    // Chỉ được gọi từ lệnh khởi tạo quản trị viên tại máy chủ, không có HTTP endpoint.
    Task BootstrapAdminAsync(string email, string password);
}
