using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Patients;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Extensions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

public class PatientService : IPatientService
{
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointments;
    private readonly ICurrentUser _currentUser;
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IValidator<CreatePatientRequest> _createValidator;
    private readonly IValidator<UpdatePatientRequest> _updateValidator;

    public PatientService(
        IPatientRepository patientRepository,
        IAppointmentRepository appointments,
        ICurrentUser currentUser,
        IClinicScheduleTransaction transaction,
        IValidator<CreatePatientRequest> createValidator,
        IValidator<UpdatePatientRequest> updateValidator)
    {
        _patientRepository = patientRepository;
        _appointments = appointments;
        _currentUser = currentUser;
        _transaction = transaction;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<List<PatientDto>> GetAllAsync()
    {
        RequireStaff();
        var patients = await _patientRepository.GetAllAsync();
        return patients.Select(MapToDto).ToList();
    }

    public async Task<PatientDto?> GetByIdAsync(int patientId)
    {
        RequireProfileAccess(patientId);
        var patient = await _patientRepository.GetByIdAsync(patientId);
        return patient is null ? null : MapToDto(patient);
    }

    public async Task<PatientDto> CreateAsync(CreatePatientRequest request)
    {
        RequireStaff();
        await _createValidator.ValidateAndThrowAsync(request);

        var patient = new Patient
        {
            FullName = request.FullName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            PhoneNumber = request.PhoneNumber.Trim(),
            Email = request.Email.TrimToNull(),
            Address = request.Address.TrimToNull(),
            CreatedAt = DateTime.UtcNow
        };
        await _patientRepository.AddAsync(patient);
        await _patientRepository.SaveChangesAsync();
        return MapToDto(patient);
    }

    public async Task<PatientDto?> UpdateAsync(int patientId, UpdatePatientRequest request)
    {
        RequireProfileAccess(patientId);
        await _updateValidator.ValidateAndThrowAsync(request);

        var patient = await _patientRepository.GetByIdTrackingAsync(patientId);
        if (patient is null)
            return null;

        patient.FullName = request.FullName.Trim();
        patient.DateOfBirth = request.DateOfBirth;
        patient.Gender = request.Gender;
        patient.PhoneNumber = request.PhoneNumber.Trim();
        patient.Email = request.Email.TrimToNull();
        patient.Address = request.Address.TrimToNull();
        patient.UpdatedAt = DateTime.UtcNow;
        await _patientRepository.SaveChangesAsync();
        return MapToDto(patient);
    }

    // Xóa mềm; chạy trong transaction để không có ai đặt lịch mới chen vào giữa lúc kiểm tra và lúc xóa.
    public async Task<bool> DeleteAsync(int patientId)
    {
        RequireStaff();

        return await _transaction.ExecuteAsync(async () =>
        {
            var patient = await _patientRepository.GetByIdTrackingAsync(patientId);
            if (patient is null)
                return false;

            if (await _appointments.HasUpcomingConfirmedAsync(ClinicClock.Now, patientId: patientId))
                throw new AppointmentConflictException("Bệnh nhân còn lịch hẹn chưa kết thúc, không thể xóa hồ sơ.");

            patient.DeletedAt = DateTime.UtcNow;
            await _patientRepository.SaveChangesAsync();
            return true;
        });
    }

    private void RequireStaff()
    {
        if (!_currentUser.IsStaff())
            throw new AccessDeniedException();
    }

    // Trả 404 thay vì 403 để không tiết lộ hồ sơ của người khác có tồn tại hay không.
    private void RequireProfileAccess(int patientId)
    {
        if (!_currentUser.IsStaff() && !_currentUser.IsPatient(patientId))
            throw new ResourceNotFoundException("Hồ sơ bệnh nhân không tồn tại.");
    }

    private static PatientDto MapToDto(Patient patient) => new()
    {
        PatientId = patient.PatientId,
        FullName = patient.FullName,
        DateOfBirth = patient.DateOfBirth,
        Gender = patient.Gender,
        PhoneNumber = patient.PhoneNumber,
        Email = patient.Email,
        Address = patient.Address,
        CreatedAt = patient.CreatedAt,
        UpdatedAt = patient.UpdatedAt
    };
}
