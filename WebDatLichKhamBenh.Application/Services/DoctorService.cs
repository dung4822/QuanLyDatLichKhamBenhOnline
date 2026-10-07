using FluentValidation;
using Microsoft.Extensions.Logging;
using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

// Ảnh đại diện lưu ở Cloudinary, dữ liệu bác sĩ lưu ở PostgreSQL. Hai nơi này không chung transaction,
// nên phải tự "dọn" ảnh khi một bên thành công còn bên kia thất bại (gọi là compensation).
public class DoctorService : IDoctorService
{
    private readonly IDoctorRepository _doctorRepository;
    private readonly ISpecialtyRepository _specialtyRepository;
    private readonly IAppointmentRepository _appointments;
    private readonly IAppointmentSlotService _appointmentSlotService;
    private readonly IImageStorageService _imageStorageService;
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IValidator<CreateDoctorRequest> _createValidator;
    private readonly IValidator<UpdateDoctorRequest> _updateValidator;
    private readonly ILogger<DoctorService> _logger;

    public DoctorService(
        IDoctorRepository doctorRepository,
        ISpecialtyRepository specialtyRepository,
        IAppointmentRepository appointments,
        IAppointmentSlotService appointmentSlotService,
        IImageStorageService imageStorageService,
        IClinicScheduleTransaction transaction,
        IValidator<CreateDoctorRequest> createValidator,
        IValidator<UpdateDoctorRequest> updateValidator,
        ILogger<DoctorService> logger)
    {
        _doctorRepository = doctorRepository;
        _specialtyRepository = specialtyRepository;
        _appointments = appointments;
        _appointmentSlotService = appointmentSlotService;
        _imageStorageService = imageStorageService;
        _transaction = transaction;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    public Task<List<DoctorDto>> GetAllAsync(int? specialtyId = null)
        => _doctorRepository.GetListDoctorAsync(specialtyId);

    public Task<DoctorDto?> GetByIdAsync(int doctorId)
        => _doctorRepository.GetDoctorByIdAsync(doctorId);

    public async Task<DoctorDto> CreateAsync(CreateDoctorRequest request, CancellationToken cancellationToken = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var specialty = await GetRequiredSpecialtyAsync(request.SpecialtyId);

        var avatar = request.Avatar is null ? null : await _imageStorageService.UploadAsync(request.Avatar, cancellationToken);
        var doctor = new Doctor
        {
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            Email = request.Email?.Trim(),
            Gender = request.Gender,
            Address = request.Address?.Trim(),
            CareerStartDate = request.CareerStartDate,
            SpecialtyId = request.SpecialtyId,
            AvatarUrl = avatar?.Url,
            AvatarStorageKey = avatar?.StorageKey,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _doctorRepository.AddAsync(doctor);
            await _doctorRepository.SaveChangesAsync();
        }
        catch
        {
            // Ảnh đã lên Cloudinary nhưng DB lưu thất bại → xóa ảnh vừa upload rồi báo lỗi như bình thường.
            await TryDeleteImageAsync(avatar?.StorageKey, "rollback ảnh mới vì không tạo được Doctor trong database");
            throw;
        }

        return MapToDto(doctor, specialty.Name);
    }

    public async Task<DoctorDto?> UpdateAsync(int doctorId, UpdateDoctorRequest request, CancellationToken cancellationToken = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        if (await _doctorRepository.GetDoctorByIdAsync(doctorId) is null)
            return null;

        // Bước 1: upload ảnh mới TRƯỚC transaction, để không giữ khóa lịch khám trong lúc chờ Cloudinary.
        var newAvatar = request.Avatar is null ? null : await _imageStorageService.UploadAsync(request.Avatar, cancellationToken);

        // Bước 2: cập nhật DB. Thất bại thì xóa ảnh mới để không để lại ảnh "mồ côi".
        string? oldAvatarKey = null;
        DoctorDto? result;
        try
        {
            result = await _transaction.ExecuteAsync(async () =>
            {
                var doctor = await _doctorRepository.GetDoctorByIdTrackingAsync(doctorId);
                if (doctor is null)
                    return null;

                var specialty = await GetRequiredSpecialtyAsync(request.SpecialtyId);
                var specialtyChanged = doctor.SpecialtyId != request.SpecialtyId;
                if (specialtyChanged && await _appointments.HasUpcomingConfirmedAsync(ClinicClock.Now, doctorId: doctorId))
                    throw new AppointmentConflictException("Bác sĩ còn lịch hẹn chưa kết thúc, chưa thể đổi chuyên khoa.");

                oldAvatarKey = doctor.AvatarStorageKey;
                doctor.FullName = request.FullName.Trim();
                doctor.PhoneNumber = request.PhoneNumber?.Trim();
                doctor.Email = request.Email?.Trim();
                doctor.Gender = request.Gender;
                doctor.Address = request.Address?.Trim();
                doctor.CareerStartDate = request.CareerStartDate;
                doctor.SpecialtyId = request.SpecialtyId;
                doctor.UpdatedAt = DateTime.UtcNow;
                if (newAvatar is not null)
                {
                    doctor.AvatarUrl = newAvatar.Url;
                    doctor.AvatarStorageKey = newAvatar.StorageKey;
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
        catch
        {
            await TryDeleteImageAsync(newAvatar?.StorageKey, "rollback ảnh mới vì không cập nhật được Doctor");
            throw;
        }

        // Bước 3: DB đã commit thành công thì mới dọn ảnh không còn dùng.
        if (result is null)
            await TryDeleteImageAsync(newAvatar?.StorageKey, "bác sĩ đã bị xóa trước khi cập nhật");
        else if (newAvatar is not null || request.RemoveAvatar)
            await TryDeleteImageAsync(oldAvatarKey, "dọn ảnh cũ sau khi transaction đã commit");
        return result;
    }

    // Xóa mềm nên giữ lại ảnh, để sau này còn có thể khôi phục bác sĩ.
    public Task<bool> DeleteAsync(int doctorId)
        => _transaction.ExecuteAsync(async () =>
        {
            var doctor = await _doctorRepository.GetDoctorByIdTrackingAsync(doctorId);
            if (doctor is null)
                return false;

            if (await _appointments.HasUpcomingConfirmedAsync(ClinicClock.Now, doctorId: doctorId))
                throw new AppointmentConflictException("Bác sĩ còn lịch hẹn chưa kết thúc, không thể xóa.");

            doctor.IsDelete = true;
            await _doctorRepository.SaveChangesAsync();
            await _appointmentSlotService.EnsureRollingWindowAsync();
            return true;
        });

    private async Task<Specialty> GetRequiredSpecialtyAsync(int specialtyId)
        => await _specialtyRepository.GetByIdAsync(specialtyId)
            ?? throw ValidationErrors.For(nameof(CreateDoctorRequest.SpecialtyId), "Chuyên khoa không tồn tại.");

    // Dọn ảnh là việc "phụ": lỗi thì chỉ ghi log, không làm hỏng kết quả của thao tác chính.
    private async Task TryDeleteImageAsync(string? storageKey, string reason)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return;

        try
        {
            // Dùng CancellationToken.None: việc dọn vẫn cần chạy kể cả khi request gốc đã bị hủy.
            await _imageStorageService.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Không thể xóa ảnh {StorageKey} khi {Reason}. Ảnh có thể bị orphan.", storageKey, reason);
        }
    }

    private static DoctorDto MapToDto(Doctor doctor, string specialtyName) => new()
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
        Experience = DateTime.UtcNow.Year - doctor.CareerStartDate.Year
    };
}
