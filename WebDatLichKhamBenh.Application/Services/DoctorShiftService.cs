using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.DoctorShifts;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

// Lịch làm việc hằng tuần của bác sĩ = danh sách các ca (Shift) bác sĩ được phân công.
public class DoctorShiftService : IDoctorShiftService
{
    private readonly IDoctorShiftRepository _doctorShiftRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IShiftRepository _shiftRepository;
    private readonly IAppointmentRepository _appointments;
    private readonly IAppointmentSlotService _appointmentSlotService;
    private readonly IClinicScheduleTransaction _transaction;
    private readonly IValidator<ReplaceDoctorShiftsRequest> _replaceValidator;

    public DoctorShiftService(
        IDoctorShiftRepository doctorShiftRepository,
        IDoctorRepository doctorRepository,
        IShiftRepository shiftRepository,
        IAppointmentRepository appointments,
        IAppointmentSlotService appointmentSlotService,
        IClinicScheduleTransaction transaction,
        IValidator<ReplaceDoctorShiftsRequest> replaceValidator)
    {
        _doctorShiftRepository = doctorShiftRepository;
        _doctorRepository = doctorRepository;
        _shiftRepository = shiftRepository;
        _appointments = appointments;
        _appointmentSlotService = appointmentSlotService;
        _transaction = transaction;
        _replaceValidator = replaceValidator;
    }

    public async Task<List<DoctorShiftDto>?> GetWeeklyScheduleAsync(int doctorId)
    {
        if (await _doctorRepository.GetDoctorByIdAsync(doctorId) is null)
            return null;

        return await _doctorShiftRepository.GetByDoctorIdAsync(doctorId);
    }

    // Giao diện gửi lên toàn bộ các ca đang được tick: ca mới tick thì thêm, ca bị bỏ tick thì xóa mềm.
    public async Task<List<DoctorShiftDto>?> ReplaceWeeklyScheduleAsync(int doctorId, ReplaceDoctorShiftsRequest request)
    {
        await _replaceValidator.ValidateAndThrowAsync(request);

        return await _transaction.ExecuteAsync(async () =>
        {
            if (await _doctorRepository.GetDoctorByIdAsync(doctorId) is null)
                return null;

            var selectedShiftIds = request.ShiftIds.ToHashSet();
            await EnsureShiftsCanBeAssignedAsync(selectedShiftIds);

            var currentAssignments = await _doctorShiftRepository.GetByDoctorIdTrackingAsync(doctorId);
            var currentShiftIds = currentAssignments.Select(x => x.ShiftId).ToHashSet();
            if (currentShiftIds.SetEquals(selectedShiftIds))
                return await _doctorShiftRepository.GetByDoctorIdAsync(doctorId);

            var removedAssignments = currentAssignments.Where(x => !selectedShiftIds.Contains(x.ShiftId)).ToList();
            if (removedAssignments.Count > 0 && await _appointments.HasUpcomingConfirmedAsync(
                    ClinicClock.Now, doctorShiftIds: removedAssignments.Select(x => x.DoctorShiftId).ToArray()))
                throw new AppointmentConflictException("Ca bị bỏ chọn đang có lịch hẹn chưa kết thúc. Hãy xử lý lịch hẹn trước.");

            // Xóa mềm để AppointmentSlot cũ vẫn còn tham chiếu được tới phân công cũ.
            var changedAt = DateTime.UtcNow;
            foreach (var assignment in removedAssignments)
            {
                assignment.IsDeleted = true;
                assignment.DeletedAt = changedAt;
                assignment.UpdatedAt = changedAt;
            }

            foreach (var shiftId in selectedShiftIds.Where(id => !currentShiftIds.Contains(id)))
                await _doctorShiftRepository.AddAsync(new DoctorShift { DoctorId = doctorId, ShiftId = shiftId, CreatedAt = changedAt });

            await _doctorShiftRepository.SaveChangesAsync();
            await _appointmentSlotService.EnsureRollingWindowAsync();
            return await _doctorShiftRepository.GetByDoctorIdAsync(doctorId);
        });
    }

    // Mọi ca được chọn phải tồn tại, đang hoạt động, và không chồng giờ nhau trong cùng một ngày.
    private async Task EnsureShiftsCanBeAssignedAsync(HashSet<int> shiftIds)
    {
        var shifts = await _shiftRepository.GetByIdsAsync(shiftIds);
        if (shifts.Count != shiftIds.Count)
            throw ValidationErrors.For(nameof(ReplaceDoctorShiftsRequest.ShiftIds), "Có ca làm việc không tồn tại.");

        var inactiveShiftIds = shifts.Where(shift => !shift.IsActive).Select(shift => shift.ShiftId).ToList();
        if (inactiveShiftIds.Count > 0)
            throw ValidationErrors.For(nameof(ReplaceDoctorShiftsRequest.ShiftIds),
                $"Không thể phân công các ca đang ngừng hoạt động: {string.Join(", ", inactiveShiftIds)}.");

        foreach (var day in shifts.GroupBy(shift => shift.DayOfWeek))
        {
            var ordered = day.OrderBy(shift => shift.StartTime).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].StartTime < ordered[i - 1].EndTime)
                    throw ValidationErrors.For(nameof(ReplaceDoctorShiftsRequest.ShiftIds),
                        $"Các ca {ordered[i - 1].ShiftId} và {ordered[i].ShiftId} bị chồng giờ vào {day.Key}.");
            }
        }
    }
}
