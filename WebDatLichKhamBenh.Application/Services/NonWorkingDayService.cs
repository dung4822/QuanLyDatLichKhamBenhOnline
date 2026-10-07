using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.NonWorkingDays;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Extensions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

// Ngày nghỉ của một bác sĩ (DoctorId có giá trị) hoặc của cả bệnh viện (DoctorId = null).
// Mỗi lần thêm/sửa/xóa đều phải đồng bộ lại khung giờ của ngày bị ảnh hưởng.
public class NonWorkingDayService : INonWorkingDayService
{
    private readonly INonWorkingDayRepository _nonWorkingDayRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IAppointmentRepository _appointments;
    private readonly IAppointmentSlotService _appointmentSlotService;
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IValidator<CreateNonWorkingDayRequest> _createValidator;
    private readonly IValidator<UpdateNonWorkingDayRequest> _updateValidator;

    public NonWorkingDayService(
        INonWorkingDayRepository nonWorkingDayRepository,
        IDoctorRepository doctorRepository,
        IAppointmentRepository appointments,
        IAppointmentSlotService appointmentSlotService,
        IClinicScheduleTransaction transaction,
        IValidator<CreateNonWorkingDayRequest> createValidator,
        IValidator<UpdateNonWorkingDayRequest> updateValidator)
    {
        _nonWorkingDayRepository = nonWorkingDayRepository;
        _doctorRepository = doctorRepository;
        _appointments = appointments;
        _appointmentSlotService = appointmentSlotService;
        _transaction = transaction;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<List<NonWorkingDayDto>> GetAllAsync(DateOnly? fromDate)
    {
        var nonWorkingDays = await _nonWorkingDayRepository.GetAllAsync(fromDate);
        return nonWorkingDays.Select(MapToDto).ToList();
    }

    public async Task<NonWorkingDayDto?> GetByIdAsync(int nonWorkingDayId)
    {
        var nonWorkingDay = await _nonWorkingDayRepository.GetByIdAsync(nonWorkingDayId);
        return nonWorkingDay is null ? null : MapToDto(nonWorkingDay);
    }

    public async Task<NonWorkingDayDto> CreateAsync(CreateNonWorkingDayRequest request)
    {
        await _createValidator.ValidateAndThrowAsync(request);

        return await _transaction.ExecuteAsync(async () =>
        {
            await EnsureCanUseDateAsync(request.Date, request.DoctorId);

            var nonWorkingDay = new NonWorkingDay
            {
                Date = request.Date,
                DoctorId = request.DoctorId,
                Reason = request.Reason.TrimToNull(),
                CreatedAt = DateTime.UtcNow
            };
            await _nonWorkingDayRepository.AddAsync(nonWorkingDay);
            await _nonWorkingDayRepository.SaveChangesAsync();

            await _appointmentSlotService.SyncRangeAsync(nonWorkingDay.Date, nonWorkingDay.Date, nonWorkingDay.DoctorId);
            return await GetSavedAsync(nonWorkingDay.NonWorkingDayId);
        });
    }

    public async Task<NonWorkingDayDto?> UpdateAsync(int nonWorkingDayId, UpdateNonWorkingDayRequest request)
    {
        await _updateValidator.ValidateAndThrowAsync(request);

        return await _transaction.ExecuteAsync(async () =>
        {
            var nonWorkingDay = await _nonWorkingDayRepository.GetByIdTrackingAsync(nonWorkingDayId);
            if (nonWorkingDay is null)
                return null;

            EnsureStillFuture(nonWorkingDay.Date);
            await EnsureCanUseDateAsync(request.Date, request.DoctorId, nonWorkingDayId);

            // Nhớ ngày cũ: khung giờ của ngày cũ phải được mở lại, ngày mới phải được khóa.
            var oldDate = nonWorkingDay.Date;
            var oldDoctorId = nonWorkingDay.DoctorId;

            nonWorkingDay.Date = request.Date;
            nonWorkingDay.DoctorId = request.DoctorId;
            nonWorkingDay.Reason = request.Reason.TrimToNull();
            nonWorkingDay.UpdatedAt = DateTime.UtcNow;
            await _nonWorkingDayRepository.SaveChangesAsync();

            await _appointmentSlotService.SyncRangeAsync(oldDate, oldDate, oldDoctorId);
            await _appointmentSlotService.SyncRangeAsync(nonWorkingDay.Date, nonWorkingDay.Date, nonWorkingDay.DoctorId);
            return await GetSavedAsync(nonWorkingDayId);
        });
    }

    public Task<bool> DeleteAsync(int nonWorkingDayId)
        => _transaction.ExecuteAsync(async () =>
        {
            var nonWorkingDay = await _nonWorkingDayRepository.GetByIdTrackingAsync(nonWorkingDayId);
            if (nonWorkingDay is null)
                return false;

            EnsureStillFuture(nonWorkingDay.Date);

            var deletedAt = DateTime.UtcNow;
            nonWorkingDay.IsDeleted = true;
            nonWorkingDay.DeletedAt = deletedAt;
            nonWorkingDay.UpdatedAt = deletedAt;
            await _nonWorkingDayRepository.SaveChangesAsync();

            await _appointmentSlotService.SyncRangeAsync(nonWorkingDay.Date, nonWorkingDay.Date, nonWorkingDay.DoctorId);
            return true;
        });

    public Task<bool> IsDoctorUnavailableAsync(int doctorId, DateOnly date)
        => _nonWorkingDayRepository.IsDoctorUnavailableAsync(doctorId, date);

    // Ba điều kiện để đặt một ngày nghỉ: bác sĩ tồn tại, chưa có ngày nghỉ trùng, không có lịch hẹn bị ảnh hưởng.
    private async Task EnsureCanUseDateAsync(DateOnly date, int? doctorId, int? excludedNonWorkingDayId = null)
    {
        if (doctorId.HasValue && await _doctorRepository.GetDoctorByIdAsync(doctorId.Value) is null)
            throw ValidationErrors.For(nameof(CreateNonWorkingDayRequest.DoctorId), "Bác sĩ không tồn tại hoặc đã bị xóa.");

        if (await _nonWorkingDayRepository.ExistsAsync(date, doctorId, excludedNonWorkingDayId))
        {
            var scope = doctorId.HasValue ? "bác sĩ này" : "cả bệnh viện";
            throw ValidationErrors.For(nameof(CreateNonWorkingDayRequest.Date), $"Đã có lịch nghỉ cho {scope} vào ngày này.");
        }

        if (await _appointments.HasUpcomingConfirmedAsync(ClinicClock.Now, doctorId: doctorId, date: date))
            throw new AppointmentConflictException("Ngày nghỉ ảnh hưởng lịch hẹn đã xác nhận. Hãy xử lý lịch hẹn trước.");
    }

    private static void EnsureStillFuture(DateOnly date)
    {
        if (date <= ClinicClock.Today)
            throw ValidationErrors.For(nameof(CreateNonWorkingDayRequest.Date),
                "Chỉ có thể sửa hoặc hủy lịch nghỉ của ngày trong tương lai.");
    }

    // Đọc lại sau khi lưu để có DoctorFullName (navigation property) trong kết quả trả về.
    private async Task<NonWorkingDayDto> GetSavedAsync(int nonWorkingDayId)
        => await GetByIdAsync(nonWorkingDayId)
            ?? throw new InvalidOperationException("Không thể đọc lịch nghỉ vừa được lưu.");

    private static NonWorkingDayDto MapToDto(NonWorkingDay nonWorkingDay) => new()
    {
        NonWorkingDayId = nonWorkingDay.NonWorkingDayId,
        Date = nonWorkingDay.Date,
        DoctorId = nonWorkingDay.DoctorId,
        DoctorFullName = nonWorkingDay.Doctor?.FullName,
        Reason = nonWorkingDay.Reason,
        CreatedAt = nonWorkingDay.CreatedAt,
        UpdatedAt = nonWorkingDay.UpdatedAt
    };
}
