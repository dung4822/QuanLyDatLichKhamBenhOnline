using WebDatLichKhamBenh.Application.DTOs.Patients;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.DTOs.Accounts;

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public CreatePatientRequest Profile { get; set; } = new();
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class CreateAccountRequest : LoginRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public AccountRole Role { get; set; }
    public int? PatientId { get; set; }
    public int? DoctorId { get; set; }
}

public class UpdateAccountAccessRequest
{
    public AccountRole Role { get; set; }
    public int? PatientId { get; set; }
    public int? DoctorId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public record LoginResponse(string AccessToken, DateTime ExpiresAt, AccountDto Account)
{
    public string TokenType => "Bearer";
}
