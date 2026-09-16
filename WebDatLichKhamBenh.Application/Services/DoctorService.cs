using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Application.DTOs.Images;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Application.Time;

namespace WebDatLichKhamBenh.Application.Services;

public class DoctorService : IDoctorService
{
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctorRepository;
    private readonly ISpecialtyRepository _specialtyRepository;
    private readonly IImageStorageService _imageStorageService;
    private readonly IAppointmentSlotService _appointmentSlotService;
    private readonly IValidator<CreateDoctorRequest> _createValidator;
    private readonly IValidator<UpdateDoctorRequest> _updateValidator;
    private readonly ILogger<DoctorService> _logger;

    public DoctorService(
        IClinicScheduleTransaction transaction,
        IAppointmentRepository appointments,
        IDoctorRepository doctorRepository,
        ISpecialtyRepository specialtyRepository,
        IImageStorageService imageStorageService,
        IAppointmentSlotService appointmentSlotService,
        IValidator<CreateDoctorRequest> createValidator,
        IValidator<UpdateDoctorRequest> updateValidator,
        ILogger<DoctorService> logger)
    {
        _transaction = transaction;
        _appointments = appointments;
        _doctorRepository = doctorRepository;
        _specialtyRepository = specialtyRepository;
        _imageStorageService = imageStorageService;
        _appointmentSlotService = appointmentSlotService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    public async Task<List<DoctorDto>> GetAllAsync(int? specialtyId = null)
    {
        return await _doctorRepository.GetListDoctorAsync(specialtyId);
    }

    public async Task<DoctorDto?> GetByIdAsync(int doctorId)
    {
        return await _doctorRepository.GetDoctorByIdAsync(doctorId);
    }

    public async Task<DoctorDto> CreateAsync(
        CreateDoctorRequest createDoctorRequest,
        CancellationToken cancellationToken = default)
    {


        //check validation du liệu nhận vào => nếu không pass => khỏi làm gì
        await _createValidator.ValidateAndThrowAsync(createDoctorRequest, cancellationToken);
        var specialty = await EnsureSpecialtyExistsAsync(createDoctorRequest.SpecialtyId);

        ImageUploadResult? uploadedAvatar = null;
        if (createDoctorRequest.Avatar is not null)
        {
            uploadedAvatar = await _imageStorageService.UploadAsync(
                createDoctorRequest.Avatar,
                cancellationToken);
        }

        var doctor = new Doctor
        {
            FullName = createDoctorRequest.FullName.Trim(),
            PhoneNumber = createDoctorRequest.PhoneNumber?.Trim(),
            Email = createDoctorRequest.Email?.Trim(),
            Gender = createDoctorRequest.Gender,
            Address = createDoctorRequest.Address?.Trim(),
            CareerStartDate = createDoctorRequest.CareerStartDate,
            SpecialtyId = createDoctorRequest.SpecialtyId,
            AvatarUrl = uploadedAvatar?.Url,
            AvatarStorageKey = uploadedAvatar?.StorageKey,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _doctorRepository.AddAsync(doctor);
            await _doctorRepository.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            // Cloudinary đã lưu thành công nhưng DB thất bại: xóa bù ảnh vừa tạo.
            await TryDeleteImageAsync(
                uploadedAvatar?.StorageKey,
                "rollback ảnh mới vì không tạo được Doctor trong database");

            throw new DataPersistenceException(
                "Không thể lưu Doctor vào database. Ảnh mới đã được rollback nếu có thể.",
                exception);
        }

        return MapToDto(doctor, specialty.Name);
    }

    public async Task<DoctorDto?> UpdateAsync(int doctorId, UpdateDoctorRequest request,
        CancellationToken cancellationToken = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        if (await _doctorRepository.GetDoctorByIdAsync(doctorId) is null)
            return null;

        // Không giữ khóa lịch khám trong lúc upload ảnh.
        ImageUploadResult? uploadedAvatar = request.Avatar is null ? null :
            await _imageStorageService.UploadAsync(request.Avatar, cancellationToken);
        string? oldAvatarStorageKey = null;
        DoctorDto? result;
        try
        {
            result = await _transaction.ExecuteAsync(async () =>
            {
                var doctor = await _doctorRepository.GetDoctorByIdTrackingAsync(doctorId);
                if (doctor is null)
                    return null;
                var specialty = await EnsureSpecialtyExistsAsync(request.SpecialtyId);
                var specialtyChanged = doctor.SpecialtyId != request.SpecialtyId;
                if (specialtyChanged && await _appointments.HasUpcomingConfirmedAsync(ClinicClock.Now, doctorId: doctorId))
                    throw new AppointmentConflictException("Bác sĩ còn lịch hẹn chưa kết thúc, chưa thể đổi chuyên khoa.");

                oldAvatarStorageKey = doctor.AvatarStorageKey;
                doctor.FullName = request.FullName.Trim();
                doctor.PhoneNumber = request.PhoneNumber?.Trim();
                doctor.Email = request.Email?.Trim();
                doctor.Gender = request.Gender;
                doctor.Address = request.Address?.Trim();
                doctor.CareerStartDate = request.CareerStartDate;
                doctor.SpecialtyId = request.SpecialtyId;
                doctor.UpdatedAt = DateTime.UtcNow;
                if (uploadedAvatar is not null)
                {
                    doctor.AvatarUrl = uploadedAvatar.Url;
                    doctor.AvatarStorageKey = uploadedAvatar.StorageKey;
                }
                else if (request.RemoveAvatar)
                {
                    doctor.AvatarUrl = null;
                    doctor.AvatarStorageKey = null;
                }

                await _doctorRepository.SaveChangesAsync();
                if (specialtyChanged)
                    await _appointmentSlotService.EnsureRollingWindowAsync(cancellationToken);
                return MapToDto(doctor, specialty.Name);
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            await TryDeleteImageAsync(uploadedAvatar?.StorageKey, "rollback ảnh mới vì không cập nhật được Doctor");
            if (exception is ValidationException or AppointmentConflictException or OperationCanceledException)
                throw;
            throw new DataPersistenceException("Không thể cập nhật Doctor trong database.", exception);
        }

        if (result is null)
            await TryDeleteImageAsync(uploadedAvatar?.StorageKey, "bác sĩ đã bị xóa trước khi cập nhật");
        else if (uploadedAvatar is not null || request.RemoveAvatar)
            await TryDeleteImageAsync(oldAvatarStorageKey, "dọn ảnh cũ sau khi transaction đã commit");
        return result;
    }

    public Task<bool> DeleteAsync(int doctorId)
        => _transaction.ExecuteAsync(() => DeleteCoreAsync(doctorId));

    private async Task<bool> DeleteCoreAsync(int doctorId)
    {
        var doctor = await _doctorRepository.GetDoctorByIdTrackingAsync(doctorId);
        if (doctor is null)
        {
            return false;
        }

        // Soft delete nên giữ ảnh để sau này còn có thể khôi phục Doctor.
        if (await _appointments.HasUpcomingConfirmedAsync(ClinicClock.Now, doctorId: doctorId))
            throw new AppointmentConflictException("Bác sĩ còn lịch hẹn chưa kết thúc, không thể xóa.");
        doctor.IsDelete = true;
        await _doctorRepository.SaveChangesAsync();
        await _appointmentSlotService.EnsureRollingWindowAsync();
        return true;
    }

    private async Task<Specialty> EnsureSpecialtyExistsAsync(int specialtyId)
    {
        var specialty = await _specialtyRepository.GetByIdAsync(specialtyId);
        if (specialty is not null)
        {
            return specialty;
        }

        throw new ValidationException(new[]
        {
            new ValidationFailure(
                nameof(CreateDoctorRequest.SpecialtyId),
                "Chuyên khoa không tồn tại.")
        });
    }

    private async Task TryDeleteImageAsync(string? storageKey, string reason)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return;
        }

        try
        {
            // Cleanup vẫn cần chạy kể cả request gốc vừa bị hủy.
            await _imageStorageService.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Không ném lỗi tiếp: thao tác DB chính đã có kết quả rõ ràng.
            _logger.LogWarning(
                exception,
                "Không thể xóa ảnh {StorageKey} khi {Reason}. Ảnh có thể bị orphan.",
                storageKey,
                reason);
        }
    }

    private static DoctorDto MapToDto(Doctor doctor, string specialtyName)
    {
        var currentYear = DateTime.UtcNow.Year;

        return new DoctorDto
        {
            DoctorId = doctor.DoctorId,
            SpecialtyId = doctor.SpecialtyId,
            SpecialtyName = specialtyName,
            FullName = doctor.FullName,
            PhoneNumber = doctor.PhoneNumber,
            Email = doctor.Email,
            Gender = doctor.Gender,
            Address = doctor.Address,
            AvatarUrl = doctor.AvatarUrl,
            CareerStartDate = doctor.CareerStartDate,
            Experience = currentYear - doctor.CareerStartDate.Year
        };
    }
}
