using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Extensions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Mappings;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private readonly IAccountRepository _accounts;
    private readonly IPatientRepository _patients;
    private readonly IDoctorRepository _doctors;
    private readonly IPasswordService _passwords;
    private readonly ICurrentUser _currentUser;
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;

    public AuthService(IAccountRepository accounts, IPatientRepository patients,
        IDoctorRepository doctors, IPasswordService passwords, ICurrentUser currentUser,
        IClinicScheduleTransaction transaction, IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator, IValidator<ChangePasswordRequest> changePasswordValidator)
    {
        _accounts = accounts;
        _patients = patients;
        _doctors = doctors;
        _passwords = passwords;
        _currentUser = currentUser;
        _transaction = transaction;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _changePasswordValidator = changePasswordValidator;
    }

    public async Task<AccountDto> RegisterAsync(RegisterRequest request)
    {
        await _registerValidator.ValidateAndThrowAsync(request);

        var account = new UserAccount
        {
            Email = request.Email.Trim(),
            NormalizedEmail = request.Email.ToNormalizedEmail(),
            DisplayName = request.Profile.FullName.Trim(),
            Role = AccountRole.Patient
        };
        account.PasswordHash = _passwords.Hash(account, request.Password);

        return await _transaction.ExecuteAsync(async () =>
        {
            if (await _accounts.GetByEmailAsync(account.NormalizedEmail) is not null)
                throw new AccountConflictException("Email đã được sử dụng.");

            // Luôn tạo hồ sơ mới, không cho người đăng ký nhận hồ sơ có sẵn của người khác.
            account.Patient = new Patient
            {
                FullName = request.Profile.FullName.Trim(),
                DateOfBirth = request.Profile.DateOfBirth,
                Gender = request.Profile.Gender,
                PhoneNumber = request.Profile.PhoneNumber.Trim(),
                Email = request.Profile.Email.TrimToNull(),
                Address = request.Profile.Address?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await _accounts.AddAsync(account);
            await _accounts.SaveChangesAsync();
            return account.ToDto();
        });
    }

    public async Task<AuthSessionDto> LoginAsync(LoginRequest request)
    {
        await _loginValidator.ValidateAndThrowAsync(request);

        // Trả null bên trong transaction để COMMIT bộ đếm sai mật khẩu trước khi trả 401.
        var result = await _transaction.ExecuteAsync<AuthSessionDto?>(async () =>
        {
            var normalizedEmail = request.Email.ToNormalizedEmail();
            var account = await _accounts.GetByEmailAsync(normalizedEmail);
            var now = DateTime.UtcNow;

            if (account is null || !account.IsActive || account.LockedUntil > now)
                return null;

            if (!_passwords.Verify(account, request.Password))
            {
                account.FailedLoginAttempts++;
                if (account.FailedLoginAttempts >= MaxFailedLoginAttempts)
                {
                    account.LockedUntil = now.Add(LockoutDuration);
                    account.FailedLoginAttempts = 0;
                }

                await _accounts.SaveChangesAsync();
                return null;
            }

            if (!await HasActiveProfileAsync(account))
                return null;

            account.FailedLoginAttempts = 0;
            account.LockedUntil = null;

            var session = new UserSession
            {
                UserAccountId = account.UserAccountId,
                CreatedAt = now,
                ExpiresAt = now.Add(SessionLifetime)
            };

            await _accounts.DeleteExpiredSessionsAsync(account.UserAccountId, now);
            await _accounts.AddSessionAsync(session);
            await _accounts.SaveChangesAsync();

            return new AuthSessionDto(session.UserSessionId, session.ExpiresAt, account.ToDto());
        });

        return result ?? throw new AuthenticationRequiredException(
            "Email hoặc mật khẩu không đúng, hoặc tài khoản tạm thời không khả dụng.");
    }

    public async Task<AuthSessionDto?> ValidateSessionAsync(Guid sessionId, Guid accountId)
    {
        var session = await _accounts.FindSessionAsync(sessionId, accountId);
        if (session is null || session.ExpiresAt <= DateTime.UtcNow || !session.UserAccount.IsActive)
            return null;

        if (!await HasActiveProfileAsync(session.UserAccount))
            return null;

        return new AuthSessionDto(session.UserSessionId, session.ExpiresAt, session.UserAccount.ToDto());
    }

    public Task LogoutAsync()
    {
        return _accounts.DeleteSessionAsync(_currentUser.SessionId, _currentUser.AccountId);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request)
    {
        await _changePasswordValidator.ValidateAndThrowAsync(request);

        await _transaction.ExecuteAsync(async () =>
        {
            var account = await _accounts.GetByIdAsync(_currentUser.AccountId)
                ?? throw new AuthenticationRequiredException();

            if (!_passwords.Verify(account, request.CurrentPassword))
                throw new AuthenticationRequiredException("Mật khẩu hiện tại không đúng.");

            account.PasswordHash = _passwords.Hash(account, request.NewPassword);
            account.UpdatedAt = DateTime.UtcNow;

            // Mật khẩu mới và thu hồi mọi phiên phải thành công hoặc rollback cùng nhau.
            await _accounts.DeleteSessionsAsync(account.UserAccountId);
            await _accounts.SaveChangesAsync();
        });
    }

    private async Task<bool> HasActiveProfileAsync(UserAccount account)
    {
        return account.Role switch
        {
            AccountRole.Patient => account.PatientId.HasValue &&
                await _patients.GetByIdAsync(account.PatientId.Value) is not null,
            AccountRole.Doctor => account.DoctorId.HasValue &&
                await _doctors.GetDoctorByIdAsync(account.DoctorId.Value) is not null,
            AccountRole.Admin or AccountRole.Receptionist => true,
            _ => false
        };
    }
}
