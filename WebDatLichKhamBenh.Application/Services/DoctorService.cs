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
    private readonly IValidator<CreateDoctorDto> _createValidator;
    private readonly IValidator<UpdateDoctorDto> _updateValidator;
    private readonly ILogger<DoctorService> _logger;

    public DoctorService(
        IDoctorRepository doctorRepository,
        ISpecialtyRepository specialtyRepository,
        IImageStorageService imageStorageService,
        IValidator<CreateDoctorDto> createValidator,
        IValidator<UpdateDoctorDto> updateValidator,
        ILogger<DoctorService> logger)
    {
        _doctorRepository = doctorRepository;
        _specialtyRepository = specialtyRepository;
        _imageStorageService = imageStorageService;
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
        CreateDoctorDto createDoctorDto,
        CancellationToken cancellationToken = default)
    {
        await _createValidator.ValidateAndThrowAsync(createDoctorDto, cancellationToken);
        await EnsureSpecialtyExistsAsync(createDoctorDto.SpecialtyId);

        ImageUploadResult? uploadedAvatar = null;
        if (createDoctorDto.Avatar is not null)
        {
            uploadedAvatar = await _imageStorageService.UploadAsync(
                createDoctorDto.Avatar,
                cancellationToken);
        }

        var doctor = new Doctor
        {
            FullName = createDoctorDto.FullName.Trim(),
            PhoneNumber = createDoctorDto.PhoneNumber?.Trim(),
            Email = createDoctorDto.Email?.Trim(),
            Gender = createDoctorDto.Gender,
            Address = createDoctorDto.Address?.Trim(),
            CareerStartDate = createDoctorDto.CareerStartDate,
            SpecialtyId = createDoctorDto.SpecialtyId,
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
        UpdateDoctorDto updateDoctorDto,
        CancellationToken cancellationToken = default)
    {
        await _updateValidator.ValidateAndThrowAsync(updateDoctorDto, cancellationToken);

        var doctor = await _doctorRepository.GetDoctorByIdTrackingAsync(doctorId);
        if (doctor is null)
        {
            return null;
        }

        await EnsureSpecialtyExistsAsync(updateDoctorDto.SpecialtyId);

        var oldAvatarStorageKey = doctor.AvatarStorageKey;
        ImageUploadResult? uploadedAvatar = null;

        // Upload trước khi sửa entity: Cloudinary lỗi thì dữ liệu Doctor và ảnh cũ vẫn nguyên vẹn.
        if (updateDoctorDto.Avatar is not null)
        {
            uploadedAvatar = await _imageStorageService.UploadAsync(
                updateDoctorDto.Avatar,
                cancellationToken);
        }

        doctor.FullName = updateDoctorDto.FullName.Trim();
        doctor.PhoneNumber = updateDoctorDto.PhoneNumber?.Trim();
        doctor.Email = updateDoctorDto.Email?.Trim();
        doctor.Gender = updateDoctorDto.Gender;
        doctor.Address = updateDoctorDto.Address?.Trim();
        doctor.CareerStartDate = updateDoctorDto.CareerStartDate;
        doctor.SpecialtyId = updateDoctorDto.SpecialtyId;
        doctor.UpdatedAt = DateTime.UtcNow;

        if (uploadedAvatar is not null)
        {
            doctor.AvatarUrl = uploadedAvatar.Url;
            doctor.AvatarStorageKey = uploadedAvatar.StorageKey;
        }
        else if (updateDoctorDto.RemoveAvatar)
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

        var avatarWasChanged = uploadedAvatar is not null || updateDoctorDto.RemoveAvatar;
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
                nameof(CreateDoctorDto.SpecialtyId),
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
