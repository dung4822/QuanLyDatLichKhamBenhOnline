using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.DTOs.Patients;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Validators;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Services;

public class AccountService(IAccountRepository accounts, IPatientRepository patients,
    IDoctorRepository doctors, IPasswordService passwords, ICurrentUser currentUser,
    IClinicScheduleTransaction transaction, IPatientService patientService) : IAccountService
{
    public async Task<AccountDto> RegisterAsync(RegisterRequest request)
    {
        await new RegisterValidator().ValidateAndThrowAsync(request);
        var account = NewAccount(request.Email, request.Profile.FullName, request.Password, AccountRole.Patient);
        return await transaction.ExecuteAsync(async () =>
        {
            await EnsureEmailAvailableAsync(account.NormalizedEmail);
            // Không nhận PatientId từ người đăng ký và không tự nối theo email/số điện thoại.
            account.Patient = new Patient
            {
                FullName = request.Profile.FullName.Trim(), DateOfBirth = request.Profile.DateOfBirth,
                Gender = request.Profile.Gender, PhoneNumber = request.Profile.PhoneNumber.Trim(),
                Email = string.IsNullOrWhiteSpace(request.Profile.Email) ? null : request.Profile.Email.Trim(),
                Address = request.Profile.Address?.Trim(), CreatedAt = DateTime.UtcNow
            };
            await accounts.AddAsync(account);
            await accounts.SaveChangesAsync();
            return Map(account);
        });
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        await new LoginValidator().ValidateAndThrowAsync(request);
        // Lưu số lần sai trước khi trả 401, không rollback bộ đếm khi đăng nhập thất bại.
        var result = await transaction.ExecuteAsync<LoginResponse?>(async () =>
        {
            var account = await accounts.GetByEmailAsync(NormalizeEmail(request.Email));
            if (account is null || !account.IsActive || account.LockedUntil > DateTime.UtcNow)
                return null;
            if (!passwords.Verify(account, request.Password))
            {
                account.FailedLoginAttempts++;
                if (account.FailedLoginAttempts >= 5)
                {
                    account.LockedUntil = DateTime.UtcNow.AddMinutes(15);
                    account.FailedLoginAttempts = 0;
                }
                await accounts.SaveChangesAsync();
                return null;
            }
            if (!await HasActiveProfileAsync(account)) return null;
            account.FailedLoginAttempts = 0;
            account.LockedUntil = null;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiresAt = DateTime.UtcNow.AddHours(8);
            await accounts.AddSessionAsync(new UserSession
            {
                UserAccountId = account.UserAccountId, TokenHash = HashToken(token), ExpiresAt = expiresAt
            });
            await accounts.SaveChangesAsync();
            return new LoginResponse(token, expiresAt, Map(account));
        });
        return result ?? throw new AuthenticationRequiredException("Email hoặc mật khẩu không đúng, hoặc tài khoản tạm thời không khả dụng.");
    }

    public async Task<UserSession?> AuthenticateAsync(string token)
    {
        if (token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c))) return null;
        var session = await accounts.FindSessionAsync(HashToken(token));
        return session is not null && session.ExpiresAt > DateTime.UtcNow && session.UserAccount.IsActive &&
            await HasActiveProfileAsync(session.UserAccount) ? session : null;
    }

    public Task LogoutAsync() => accounts.DeleteSessionAsync(currentUser.SessionId, currentUser.AccountId);

    public async Task ChangePasswordAsync(ChangePasswordRequest request)
    {
        await new ChangePasswordValidator().ValidateAndThrowAsync(request);
        await transaction.ExecuteAsync(async () =>
        {
            var account = await GetCurrentAccountAsync();
            if (!passwords.Verify(account, request.CurrentPassword))
                throw new AuthenticationRequiredException("Mật khẩu hiện tại không đúng.");
            account.PasswordHash = passwords.Hash(account, request.NewPassword);
            account.UpdatedAt = DateTime.UtcNow;
            await accounts.DeleteSessionsAsync(account.UserAccountId);
            await accounts.SaveChangesAsync();
        });
    }

    public async Task<MyProfileDto> GetMyProfileAsync()
    {
        var account = await GetCurrentAccountAsync();
        var patient = account.PatientId.HasValue ? await patientService.GetByIdAsync(account.PatientId.Value) : null;
        var doctor = account.DoctorId.HasValue ? await doctors.GetDoctorByIdAsync(account.DoctorId.Value) : null;
        return new MyProfileDto(Map(account), patient, doctor);
    }

    public async Task<PatientDto> UpdateMyPatientProfileAsync(UpdatePatientRequest request)
    {
        if (currentUser.Role != AccountRole.Patient || !currentUser.PatientId.HasValue)
            throw new AccessDeniedException();
        return await patientService.UpdateAsync(currentUser.PatientId.Value, request)
            ?? throw new ResourceNotFoundException("Hồ sơ bệnh nhân không tồn tại.");
    }

    public async Task<AccountDto> CreateAsync(CreateAccountRequest request)
    {
        RequireAdmin();
        await new CreateAccountValidator().ValidateAndThrowAsync(request);
        var account = NewAccount(request.Email, request.DisplayName, request.Password, request.Role);
        return await transaction.ExecuteAsync(async () =>
        {
            await EnsureEmailAvailableAsync(account.NormalizedEmail);
            await ValidateLinksAsync(request.Role, request.PatientId, request.DoctorId);
            account.PatientId = request.PatientId;
            account.DoctorId = request.DoctorId;
            await accounts.AddAsync(account);
            await accounts.SaveChangesAsync();
            return Map(account);
        });
    }

    public async Task<List<AccountDto>> GetPageAsync(int page, int pageSize)
    {
        RequireAdmin();
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100)
            throw new ValidationException("Page phải từ 1 đến 1000000; PageSize từ 1 đến 100.");
        return (await accounts.GetPageAsync(page, pageSize)).Select(Map).ToList();
    }

    public Task<AccountDto> UpdateAccessAsync(Guid id, UpdateAccountAccessRequest request)
    {
        RequireAdmin();
        return transaction.ExecuteAsync(async () =>
        {
            var account = await accounts.GetByIdAsync(id) ?? throw new ResourceNotFoundException("Tài khoản không tồn tại.");
            await ValidateLinksAsync(request.Role, request.PatientId, request.DoctorId, id);
            if (account.Role == AccountRole.Admin && account.IsActive &&
                (request.Role != AccountRole.Admin || !request.IsActive) && await accounts.CountActiveAdminsAsync() <= 1)
                throw new AccountConflictException("Phải giữ ít nhất một Admin đang hoạt động.");
            account.Role = request.Role;
            account.PatientId = request.PatientId;
            account.DoctorId = request.DoctorId;
            account.IsActive = request.IsActive;
            account.UpdatedAt = DateTime.UtcNow;
            await accounts.DeleteSessionsAsync(id);
            await accounts.SaveChangesAsync();
            return Map(account);
        });
    }

    public async Task BootstrapAdminAsync(string email, string password)
    {
        var request = new CreateAccountRequest { Email = email, Password = password, DisplayName = "Quản trị viên", Role = AccountRole.Admin };
        await new CreateAccountValidator().ValidateAndThrowAsync(request);
        var account = NewAccount(email, request.DisplayName, password, AccountRole.Admin);
        await transaction.ExecuteAsync(async () =>
        {
            if (await accounts.CountActiveAdminsAsync() > 0)
                throw new AccountConflictException("Đã có Admin; lệnh bootstrap chỉ dùng cho lần khởi tạo đầu tiên.");
            await EnsureEmailAvailableAsync(account.NormalizedEmail);
            await accounts.AddAsync(account);
            await accounts.SaveChangesAsync();
        });
    }

    private async Task ValidateLinksAsync(AccountRole role, int? patientId, int? doctorId, Guid? excludeId = null)
    {
        if (!Enum.IsDefined(role) ||
            (role == AccountRole.Patient && (!patientId.HasValue || doctorId.HasValue)) ||
            (role == AccountRole.Doctor && (!doctorId.HasValue || patientId.HasValue)) ||
            (role is AccountRole.Admin or AccountRole.Receptionist && (patientId.HasValue || doctorId.HasValue)))
            throw new ValidationException("Patient phải có PatientId; Doctor phải có DoctorId; Admin/Receptionist không liên kết hồ sơ.");
        if (patientId.HasValue && await patients.GetByIdAsync(patientId.Value) is null)
            throw new ResourceNotFoundException("Bệnh nhân không tồn tại hoặc đã xóa.");
        if (doctorId.HasValue && await doctors.GetDoctorByIdAsync(doctorId.Value) is null)
            throw new ResourceNotFoundException("Bác sĩ không tồn tại hoặc đã xóa.");
        if (await accounts.LinkExistsAsync(patientId, doctorId, excludeId))
            throw new AccountConflictException("Hồ sơ này đã liên kết với tài khoản khác.");
    }

    private async Task<bool> HasActiveProfileAsync(UserAccount account) => account.Role switch
    {
        AccountRole.Patient => account.PatientId.HasValue && await patients.GetByIdAsync(account.PatientId.Value) is not null,
        AccountRole.Doctor => account.DoctorId.HasValue && await doctors.GetDoctorByIdAsync(account.DoctorId.Value) is not null,
        AccountRole.Admin or AccountRole.Receptionist => true,
        _ => false
    };

    private async Task<UserAccount> GetCurrentAccountAsync() => await accounts.GetByIdAsync(currentUser.AccountId)
        ?? throw new AuthenticationRequiredException();
    private void RequireAdmin() { if (currentUser.Role != AccountRole.Admin) throw new AccessDeniedException(); }
    private async Task EnsureEmailAvailableAsync(string email)
    {
        if (await accounts.GetByEmailAsync(email) is not null)
            throw new AccountConflictException("Email đã được sử dụng.");
    }
    private UserAccount NewAccount(string email, string name, string password, AccountRole role)
    {
        var account = new UserAccount { Email = email.Trim(), NormalizedEmail = NormalizeEmail(email), DisplayName = name.Trim(), Role = role };
        account.PasswordHash = passwords.Hash(account, password);
        return account;
    }
    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static AccountDto Map(UserAccount x) => new(x.UserAccountId, x.Email, x.DisplayName, x.Role, x.PatientId, x.DoctorId, x.IsActive);
}
