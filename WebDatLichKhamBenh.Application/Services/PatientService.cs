using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Patients;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

public class PatientService : IPatientService
{
    private readonly IPatientRepository _patientRepository;
    private readonly IValidator<CreatePatientRequest> _createValidator;
    private readonly IValidator<UpdatePatientRequest> _updateValidator;

    public PatientService(
        IPatientRepository patientRepository,
        IValidator<CreatePatientRequest> createValidator,
        IValidator<UpdatePatientRequest> updateValidator)
    {
        _patientRepository = patientRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<List<PatientDto>> GetAllAsync()
    {
        var patients = await _patientRepository.GetAllAsync();
        return patients.Select(MapToDto).ToList();
    }

    public async Task<PatientDto?> GetByIdAsync(int patientId)
    {
        var patient = await _patientRepository.GetByIdAsync(patientId);
        return patient is null ? null : MapToDto(patient);
    }

    public async Task<PatientDto> CreateAsync(CreatePatientRequest createPatientRequest)
    {
        await _createValidator.ValidateAndThrowAsync(createPatientRequest);

        var patient = new Patient
        {
            FullName = createPatientRequest.FullName.Trim(),
            DateOfBirth = createPatientRequest.DateOfBirth,
            Gender = createPatientRequest.Gender,
            PhoneNumber = createPatientRequest.PhoneNumber.Trim(),
            Email = NormalizeOptional(createPatientRequest.Email),
            Address = NormalizeOptional(createPatientRequest.Address),
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _patientRepository.AddAsync(patient);
            await _patientRepository.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            throw new DataPersistenceException(
                "Không thể lưu Patient vào database.",
                exception);
        }

        return MapToDto(patient);
    }

    public async Task<PatientDto?> UpdateAsync(
        int patientId,
        UpdatePatientRequest updatePatientRequest)
    {
        await _updateValidator.ValidateAndThrowAsync(updatePatientRequest);

        var patient = await _patientRepository.GetByIdTrackingAsync(patientId);
        if (patient is null)
        {
            return null;
        }

        patient.FullName = updatePatientRequest.FullName.Trim();
        patient.DateOfBirth = updatePatientRequest.DateOfBirth;
        patient.Gender = updatePatientRequest.Gender;
        patient.PhoneNumber = updatePatientRequest.PhoneNumber.Trim();
        patient.Email = NormalizeOptional(updatePatientRequest.Email);
        patient.Address = NormalizeOptional(updatePatientRequest.Address);
        patient.UpdatedAt = DateTime.UtcNow;

        await SaveChangesAsync("Không thể cập nhật Patient trong database.");
        return MapToDto(patient);
    }

    public async Task<bool> DeleteAsync(int patientId)
    {
        var patient = await _patientRepository.GetByIdTrackingAsync(patientId);
        if (patient is null)
        {
            return false;
        }

        patient.DeletedAt = DateTime.UtcNow;
        await SaveChangesAsync("Không thể xóa Patient trong database.");
        return true;
    }

    private async Task SaveChangesAsync(string errorMessage)
    {
        try
        {
            await _patientRepository.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            throw new DataPersistenceException(errorMessage, exception);
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static PatientDto MapToDto(Patient patient)
    {
        return new PatientDto
        {
            PatientId = patient.PatientId,
            FullName = patient.FullName,
            DateOfBirth = patient.DateOfBirth,
            Gender = patient.Gender,
            PhoneNumber = patient.PhoneNumber,
            Email = patient.Email,
            Address = patient.Address,
            CreatedAt = patient.CreatedAt,
            UpdatedAt = patient.UpdatedAt,
        };
    }
}
