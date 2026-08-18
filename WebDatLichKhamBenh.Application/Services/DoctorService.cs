using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Application.DTOs.Images;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

public class DoctorService : IDoctorService
{
    private readonly IDoctorRepository _doctorRepository;
    private readonly ISpecialtyRepository _specialtyRepository;
    private readonly IImageStorageService _imageStorageService;
    private readonly IAppointmentSlotService _appointmentSlotService;
    private readonly IValidator<CreateDoctorRequest> _createValidator;
    private readonly IValidator<UpdateDoctorRequest> _updateValidator;
    private readonly ILogger<DoctorService> _logger;

    public DoctorService(
        IDoctorRepository doctorRepository,
        ISpecialtyRepository specialtyRepository,
        IImageStorageService imageStorageService,
        IAppointmentSlotService appointmentSlotService,
        IValidator<CreateDoctorRequest> createValidator,
        IValidator<UpdateDoctorRequest> updateValidator,
        ILogger<DoctorService> logger)
    {
        _doctorRepository = doctorRepository;
        _specialtyRepository = specialtyRepository;
        _imageStorageService = imageStorageService;
        _appointmentSlotService = appointmentSlotService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    public async Task<List<DoctorDto>> GetAllAsync()
    {
        return await _doctorRepository.GetListDoctorAsync();
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
        await EnsureSpecialtyExistsAsync(createDoctorRequest.SpecialtyId);

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

        return MapToDto(doctor);
    }

    public async Task<DoctorDto?> UpdateAsync(
        int doctorId,
        UpdateDoctorRequest updateDoctorRequest,
        CancellationToken cancellationToken = default)
    {
        await _updateValidator.ValidateAndThrowAsync(updateDoctorRequest, cancellationToken);

        var doctor = await _doctorRepository.GetDoctorByIdTrackingAsync(doctorId);
        if (doctor is null)
        {
            return null;
        }

        await EnsureSpecialtyExistsAsync(updateDoctorRequest.SpecialtyId);

        var oldAvatarStorageKey = doctor.AvatarStorageKey;
        ImageUploadResult? uploadedAvatar = null;

        // Upload trước khi sửa entity: Cloudinary lỗi thì dữ liệu Doctor và ảnh cũ vẫn nguyên vẹn.
        if (updateDoctorRequest.Avatar is not null)
        {
            uploadedAvatar = await _imageStorageService.UploadAsync(
                updateDoctorRequest.Avatar,
                cancellationToken);
        }

        doctor.FullName = updateDoctorRequest.FullName.Trim();
        doctor.PhoneNumber = updateDoctorRequest.PhoneNumber?.Trim();
        doctor.Email = updateDoctorRequest.Email?.Trim();
        doctor.Gender = updateDoctorRequest.Gender;
        doctor.Address = updateDoctorRequest.Address?.Trim();
        doctor.CareerStartDate = updateDoctorRequest.CareerStartDate;
        doctor.SpecialtyId = updateDoctorRequest.SpecialtyId;
        doctor.UpdatedAt = DateTime.UtcNow;

        if (uploadedAvatar is not null)
        {
            doctor.AvatarUrl = uploadedAvatar.Url;
            doctor.AvatarStorageKey = uploadedAvatar.StorageKey;
        }
        else if (updateDoctorRequest.RemoveAvatar)
        {
            doctor.AvatarUrl = null;
            doctor.AvatarStorageKey = null;
        }

        try
        {
            await _doctorRepository.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            // DB chưa nhận URL mới nên ảnh cũ vẫn là ảnh hợp lệ; chỉ cần xóa bù ảnh mới.
            await TryDeleteImageAsync(
                uploadedAvatar?.StorageKey,
                "rollback ảnh mới vì không cập nhật được Doctor trong database");

            throw new DataPersistenceException(
                "Không thể cập nhật Doctor trong database. Ảnh mới đã được rollback nếu có thể.",
                exception);
        }

        var avatarWasChanged = uploadedAvatar is not null || updateDoctorRequest.RemoveAvatar;
        if (avatarWasChanged)
        {
            // Chỉ xóa ảnh cũ sau khi DB đã trỏ sang ảnh mới (hoặc đã bỏ ảnh) thành công.
            await TryDeleteImageAsync(
                oldAvatarStorageKey,
                "dọn ảnh cũ sau khi cập nhật Doctor thành công");
        }

        return MapToDto(doctor);
    }

    public async Task<bool> DeleteAsync(int doctorId)
    {
        var doctor = await _doctorRepository.GetDoctorByIdTrackingAsync(doctorId);
        if (doctor is null)
        {
            return false;
        }

        // Soft delete nên giữ ảnh để sau này còn có thể khôi phục Doctor.
        doctor.IsDelete = true;
        await _doctorRepository.SaveChangesAsync();
        await _appointmentSlotService.EnsureRollingWindowAsync();
        return true;
    }

    private async Task EnsureSpecialtyExistsAsync(int specialtyId)
    {
        if (await _specialtyRepository.ExistsAsync(specialtyId))
        {
            return;
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

    private static DoctorDto MapToDto(Doctor doctor)
    {
        var currentYear = DateTime.UtcNow.Year;

        return new DoctorDto
        {
            DoctorId = doctor.DoctorId,
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
