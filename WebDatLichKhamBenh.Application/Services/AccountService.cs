using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.DTOs.Patients;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Extensions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Validators;
using WebDatLichKhamBenh.Application.Mappings;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accounts;
    private readonly IPatientRepository _patients;
    private readonly IDoctorRepository _doctors;
    private readonly IPasswordService _passwords;
    private readonly ICurrentUser _currentUser;
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IPatientService _patientService;
    private readonly IValidator<CreateAccountRequest> _createValidator;

    public AccountService(IAccountRepository accounts, IPatientRepository patients,
        IDoctorRepository doctors, IPasswordService passwords, ICurrentUser currentUser,
        IClinicScheduleTransaction transaction, IPatientService patientService,
        IValidator<CreateAccountRequest> createValidator)
    {
        _accounts = accounts;
        _patients = patients;
        _doctors = doctors;
        _passwords = passwords;
        _currentUser = currentUser;
        _transaction = transaction;
        _patientService = patientService;
        _createValidator = createValidator;
    }

    public async Task<MyProfileDto> GetMyProfileAsync()
    {
        var account = await GetCurrentAccountAsync();
        var patient = account.PatientId.HasValue ? await _patientService.GetByIdAsync(account.PatientId.Value) : null;
        var doctor = account.DoctorId.HasValue ? await _doctors.GetDoctorByIdAsync(account.DoctorId.Value) : null;
        return new MyProfileDto(account.ToDto(), patient, doctor);
    }

    public async Task<PatientDto> UpdateMyPatientProfileAsync(UpdatePatientRequest request)
    {
        if (_currentUser.Role != AccountRole.Patient || !_currentUser.PatientId.HasValue)
            throw new AccessDeniedException();
        return await _patientService.UpdateAsync(_currentUser.PatientId.Value, request)
            ?? throw new ResourceNotFoundException("Hồ sơ bệnh nhân không tồn tại.");
    }

    public async Task<AccountDto> CreateAsync(CreateAccountRequest request)
    {
        RequireAdmin();
        await _createValidator.ValidateAndThrowAsync(request);
        var account = NewAccount(request.Email, request.DisplayName, request.Password, request.Role);
        return await _transaction.ExecuteAsync(async () =>
        {
            await EnsureEmailAvailableAsync(account.NormalizedEmail);
            await ValidateLinksAsync(request.Role, request.PatientId, request.DoctorId);
            account.PatientId = request.PatientId;
            account.DoctorId = request.DoctorId;
            await _accounts.AddAsync(account);
            await _accounts.SaveChangesAsync();
            return account.ToDto();
        });
    }

    public async Task<List<AccountDto>> GetPageAsync(int page, int pageSize)
    {
        RequireAdmin();
        PagingRules.EnsureValid(page, pageSize);
        return (await _accounts.GetPageAsync(page, pageSize)).Select(account => account.ToDto()).ToList();
    }

    public Task<AccountDto> UpdateAccessAsync(Guid id, UpdateAccountAccessRequest request)
    {
        RequireAdmin();
        return _transaction.ExecuteAsync(async () =>
        {
            var account = await _accounts.GetByIdAsync(id) ?? throw new ResourceNotFoundException("Tài khoản không tồn tại.");
            await ValidateLinksAsync(request.Role, request.PatientId, request.DoctorId, id);
            if (account.Role == AccountRole.Admin && account.IsActive &&
                (request.Role != AccountRole.Admin || !request.IsActive) && await _accounts.CountActiveAdminsAsync() <= 1)
                throw new AccountConflictException("Phải giữ ít nhất một Admin đang hoạt động.");
            account.Role = request.Role;
            account.PatientId = request.PatientId;
            account.DoctorId = request.DoctorId;
            account.IsActive = request.IsActive;
            account.UpdatedAt = DateTime.UtcNow;
            await _accounts.DeleteSessionsAsync(id);
            await _accounts.SaveChangesAsync();
            return account.ToDto();
        });
    }

    public async Task BootstrapAdminAsync(string email, string password)
    {
        var request = new CreateAccountRequest
        {
            Email = email,
            Password = password,
            DisplayName = "Quản trị viên",
            Role = AccountRole.Admin
        };
        await _createValidator.ValidateAndThrowAsync(request);
        var account = NewAccount(email, request.DisplayName, password, AccountRole.Admin);
        await _transaction.ExecuteAsync(async () =>
        {
            if (await _accounts.CountActiveAdminsAsync() > 0)
                throw new AccountConflictException("Đã có Admin; lệnh bootstrap chỉ dùng cho lần khởi tạo đầu tiên.");
            await EnsureEmailAvailableAsync(account.NormalizedEmail);
            await _accounts.AddAsync(account);
            await _accounts.SaveChangesAsync();
        });
    }

    private async Task ValidateLinksAsync(AccountRole role, int? patientId, int? doctorId, Guid? excludeId = null)
    {
        if (!Enum.IsDefined(role) ||
            (role == AccountRole.Patient && (!patientId.HasValue || doctorId.HasValue)) ||
            (role == AccountRole.Doctor && (!doctorId.HasValue || patientId.HasValue)) ||
            (role is AccountRole.Admin or AccountRole.Receptionist && (patientId.HasValue || doctorId.HasValue)))
            throw new ValidationException("Patient phải có PatientId; Doctor phải có DoctorId; Admin/Receptionist không liên kết hồ sơ.");
        if (patientId.HasValue && await _patients.GetByIdAsync(patientId.Value) is null)
            throw new ResourceNotFoundException("Bệnh nhân không tồn tại hoặc đã xóa.");
        if (doctorId.HasValue && await _doctors.GetDoctorByIdAsync(doctorId.Value) is null)
            throw new ResourceNotFoundException("Bác sĩ không tồn tại hoặc đã xóa.");
        if (await _accounts.LinkExistsAsync(patientId, doctorId, excludeId))
            throw new AccountConflictException("Hồ sơ này đã liên kết với tài khoản khác.");
    }

    private async Task<UserAccount> GetCurrentAccountAsync() => await _accounts.GetByIdAsync(_currentUser.AccountId)
        ?? throw new AuthenticationRequiredException();

    private void RequireAdmin()
    {
        if (_currentUser.Role != AccountRole.Admin)
            throw new AccessDeniedException();
    }

    private async Task EnsureEmailAvailableAsync(string email)
    {
        if (await _accounts.GetByEmailAsync(email) is not null)
            throw new AccountConflictException("Email đã được sử dụng.");
    }
    private UserAccount NewAccount(string email, string name, string password, AccountRole role)
    {
        var account = new UserAccount
        {
            Email = email.Trim(),
            NormalizedEmail = email.ToNormalizedEmail(),
            DisplayName = name.Trim(),
            Role = role
        };
        account.PasswordHash = _passwords.Hash(account, password);
        return account;
    }
}
