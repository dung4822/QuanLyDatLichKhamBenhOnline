using WebDatLichKhamBenh.Application.DTOs.Accounts;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IAuthService
{
    Task<AccountDto> RegisterAsync(RegisterRequest request);
    Task<AuthSessionDto> LoginAsync(LoginRequest request);
    Task<AuthSessionDto?> ValidateSessionAsync(Guid sessionId, Guid accountId);
    Task LogoutAsync();
    Task ChangePasswordAsync(ChangePasswordRequest request);
}
