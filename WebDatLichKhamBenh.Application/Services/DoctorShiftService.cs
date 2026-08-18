using FluentValidation;
using FluentValidation.Results;
using WebDatLichKhamBenh.Application.DTOs.DoctorShifts;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

public class DoctorShiftService : IDoctorShiftService
{
    private readonly IDoctorShiftRepository _doctorShiftRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IShiftRepository _shiftRepository;
    private readonly IAppointmentSlotService _appointmentSlotService;
    private readonly IValidator<ReplaceDoctorShiftsRequest> _replaceValidator;

    public DoctorShiftService(
        IDoctorShiftRepository doctorShiftRepository,
        IDoctorRepository doctorRepository,
        IShiftRepository shiftRepository,
        IAppointmentSlotService appointmentSlotService,
        IValidator<ReplaceDoctorShiftsRequest> replaceValidator)
    {
        _doctorShiftRepository = doctorShiftRepository;
        _doctorRepository = doctorRepository;
        _shiftRepository = shiftRepository;
        _appointmentSlotService = appointmentSlotService;
        _replaceValidator = replaceValidator;
    }

    public async Task<List<DoctorShiftDto>?> GetWeeklyScheduleAsync(int doctorId)
    {
        var doctor = await _doctorRepository.GetDoctorByIdTrackingAsync(doctorId);
        if (doctor is null)
        {
            return null;
        }

        var doctorShifts = await _doctorShiftRepository.GetByDoctorIdAsync(doctorId);
        return doctorShifts;
    }
    //thực ra hàm này chính là biến thể Update kiểu nhiều
    public async Task<List<DoctorShiftDto>?> ReplaceWeeklyScheduleAsync(
        int doctorId,
        ReplaceDoctorShiftsRequest replaceDoctorShiftsRequest)
    {
        //1. Validate dữ liệu vào
        await _replaceValidator.ValidateAndThrowAsync(replaceDoctorShiftsRequest);

        //2. Check doctor tồn tại
        var doctor =await _doctorRepository.GetDoctorByIdAsync(doctorId);

        if(doctor is null)
        {
            return null;
        }


        //3. Check Shift
        var selectedShiftIds = replaceDoctorShiftsRequest.ShiftIds.ToHashSet();

        var selectedShifts = await _shiftRepository.GetByIdsAsync(selectedShiftIds);

        if (selectedShifts.Count !=  selectedShiftIds.Count)
        {
            throw CreateValidationException(nameof(ReplaceDoctorShiftsRequest.ShiftIds), "Không thể lưu ca làm việc ở đây được vì bạn gửi sai Dữ liệu Ca làm");
        }

        //Query xem ca đung nhưng bị ngừng thì => sai luôn
        var inactiveShiftIds =  selectedShifts.Where(shift => !shift.IsActive)
            .Select(shift => shift.ShiftId)
            .ToHashSet();

        if (inactiveShiftIds.Count > 0)
        {
            throw CreateValidationException(nameof(ReplaceDoctorShiftsRequest.ShiftIds), $"Không thể phân công các ca đang ngừng hoạt động: {string.Join(", ", inactiveShiftIds)}.");
        }

        EnsureNoOverlappingShifts(selectedShifts);


        //đoạn này thực chất nó đang lấy ra danh sách doctorshift => tại sao ở đây ta không viết luôn một hàm lấy Id shift thôi ta?
        var currentDoctorShifts = await _doctorShiftRepository.GetByDoctorIdTrackingAsync(doctorId);
        var currentShiftIds = currentDoctorShifts
            .Select(doctorShift => doctorShift.ShiftId)
            .ToHashSet();
        var changedAt = DateTime.UtcNow;

        // Các ca bị bỏ tick được xóa mềm để còn lịch sử cho AppointmentSlot về sau.
        foreach (var doctorShift in currentDoctorShifts.Where(
                     doctorShift => !selectedShiftIds.Contains(doctorShift.ShiftId)))
        {
            doctorShift.IsDeleted = true;
            doctorShift.DeletedAt = changedAt;
            doctorShift.UpdatedAt = changedAt;
        }

        foreach (var shiftId in selectedShiftIds.Where(shiftId => !currentShiftIds.Contains(shiftId)))
        {
            await _doctorShiftRepository.AddAsync(new DoctorShift
            {
                DoctorId = doctor.DoctorId,
                ShiftId = shiftId,
                CreatedAt = changedAt
            });
        }

        if (!currentShiftIds.SetEquals(selectedShiftIds))
        {
            try
            {
                // EF Core lưu toàn bộ thay đổi của một SaveChangesAsync trong cùng transaction.
                await _doctorShiftRepository.SaveChangesAsync();
                await _appointmentSlotService.EnsureRollingWindowAsync();
            }
            catch (Exception exception)
            {
                throw new DataPersistenceException(
                    "Không thể cập nhật lịch làm việc tuần của bác sĩ trong database.",
                    exception);
            }
        }

        var updatedDoctorShifts = await _doctorShiftRepository.GetByDoctorIdAsync(doctorId);
        return updatedDoctorShifts;
    }

    private static ValidationException CreateValidationException(string propertyName, string errorMessage)
    {
        return new ValidationException(new[]
        {
            new ValidationFailure(propertyName, errorMessage)
        });
    }

    private static void EnsureNoOverlappingShifts(IEnumerable<Shift> shifts)
    {
        foreach (var day in shifts.GroupBy(shift => shift.DayOfWeek))
        {
            var orderedShifts = day.OrderBy(shift => shift.StartTime).ToList();
            for (var index = 1; index < orderedShifts.Count; index++)
            {
                var previous = orderedShifts[index - 1];
                var current = orderedShifts[index];
                if (current.StartTime < previous.EndTime)
                {
                    throw CreateValidationException(
                        nameof(ReplaceDoctorShiftsRequest.ShiftIds),
                        $"Các ca {previous.ShiftId} và {current.ShiftId} bị chồng giờ vào {day.Key}.");
                }
            }
        }
    }

    private static DoctorShiftDto MapToDto(DoctorShift doctorShift)
    {
        return new DoctorShiftDto
        {
            DoctorShiftId = doctorShift.DoctorShiftId,
            DoctorId = doctorShift.DoctorId,
            DoctorFullName = doctorShift.Doctor.FullName,
            ShiftId = doctorShift.ShiftId,
            ShiftName = doctorShift.Shift.Name,
            DayOfWeek = doctorShift.Shift.DayOfWeek,
            StartTime = doctorShift.Shift.StartTime,
            EndTime = doctorShift.Shift.EndTime,
            CreatedAt = doctorShift.CreatedAt,
            UpdatedAt = doctorShift.UpdatedAt
        };
    }
}
