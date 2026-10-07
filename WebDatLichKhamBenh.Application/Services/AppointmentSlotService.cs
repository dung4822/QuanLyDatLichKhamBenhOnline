using WebDatLichKhamBenh.Application.DTOs.AppointmentSlots;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Services;

// Khung giờ khám (slot) được SINH RA từ lịch làm việc: ca (Shift) × bác sĩ được phân công (DoctorShift) × ngày.
// Mỗi khi lịch làm việc hoặc ngày nghỉ thay đổi, gọi SyncRangeAsync để bảng AppointmentSlots khớp lại.
public class AppointmentSlotService : IAppointmentSlotService
{
    private const string RemovedFromScheduleReason = "Không còn trong lịch làm việc.";
    private const string OverlapsBookingReason = "Khoảng giờ chồng với lịch hẹn đã đặt.";

    private readonly IClinicScheduleTransaction _transaction;
    private readonly IAppointmentSlotRepository _appointmentSlotRepository;
    private readonly IDoctorRepository _doctorRepository;

    public AppointmentSlotService(
        IClinicScheduleTransaction transaction,
        IAppointmentSlotRepository appointmentSlotRepository,
        IDoctorRepository doctorRepository)
    {
        _transaction = transaction;
        _appointmentSlotRepository = appointmentSlotRepository;
        _doctorRepository = doctorRepository;
    }

    public async Task<List<AppointmentSlotDto>?> GetAvailableSlotsAsync(int doctorId, DateOnly? fromDate, DateOnly? toDate)
    {
        if (await _doctorRepository.GetDoctorByIdAsync(doctorId) is null)
            return null;

        var today = ClinicClock.Today;
        var lastDay = today.AddDays(ClinicSchedule.BookingWindowDays - 1);
        var from = fromDate ?? today;
        var to = toDate ?? lastDay;
        if (from < today || to > lastDay || to < from)
            throw ValidationErrors.For("dateRange", "Khoảng ngày phải nằm trong 7 ngày từ hôm nay theo giờ Việt Nam.");

        // Repository đã lọc slot Available trong khoảng ngày; ở đây bỏ thêm các slot hôm nay đã qua giờ.
        var slots = await _appointmentSlotRepository.GetAvailableSlotsAsync(doctorId, from, to);
        var now = ClinicClock.Now;
        return slots
            .Where(slot => slot.Date.ToDateTime(slot.StartTime) > now)
            .Select(MapToDto)
            .ToList();
    }

    // Đảm bảo luôn có slot cho 7 ngày tới. Gọi định kỳ (background service) và sau mỗi thay đổi lịch.
    public Task EnsureRollingWindowAsync(CancellationToken cancellationToken = default)
        => SyncRangeAsync(ClinicClock.Today, ClinicSchedule.LastBookableDate, null, cancellationToken);

    // Đồng bộ slot trong [fromDate, toDate] (chỉ phần nằm trong cửa sổ 7 ngày), cho một hoặc mọi bác sĩ.
    // Slot đã Booked KHÔNG BAO GIỜ bị sửa, để không ảnh hưởng lịch hẹn của bệnh nhân.
    public Task SyncRangeAsync(DateOnly fromDate, DateOnly toDate, int? doctorId = null,
        CancellationToken cancellationToken = default)
        => _transaction.ExecuteAsync(async () =>
        {
            if (toDate < fromDate)
                throw new ArgumentException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.", nameof(toDate));

            // Chỉ đồng bộ phần nằm trong cửa sổ đặt lịch [hôm nay, hôm nay + 6].
            var today = ClinicClock.Today;
            var lastDay = today.AddDays(ClinicSchedule.BookingWindowDays - 1);
            var from = fromDate > today ? fromDate : today;
            var to = toDate < lastDay ? toDate : lastDay;
            if (from > to)
                return;

            cancellationToken.ThrowIfCancellationRequested();
            // Các repository dùng chung một DbContext; EF Core không cho chạy song song nên query tuần tự.
            var schedules = await _appointmentSlotRepository.GetActiveDoctorShiftsAsync(doctorId);
            var nonWorkingDays = await _appointmentSlotRepository.GetNonWorkingDaysAsync(from, to);
            var existingSlots = await _appointmentSlotRepository.GetSlotsTrackingAsync(from, to, doctorId);

            // Bước 1: từ lịch làm việc, tính ra các slot "nên có".
            var desiredSlots = BuildDesiredSlots(schedules, from, to);
            var existingByKey = existingSlots.ToDictionary(slot => (slot.DoctorId, slot.Date, slot.StartTime));
            var bookedSlots = existingSlots.Where(slot => slot.Status == AppointmentSlotStatus.Booked).ToList();
            var changedAt = DateTime.UtcNow;
            var newSlots = new List<AppointmentSlot>();
            var hasChanges = false;

            foreach (var desired in desiredSlots.Values)
            {
                var unavailableReason = GetDayOffReason(nonWorkingDays, desired.DoctorId, desired.Date)
                    ?? (OverlapsBooking(desired, bookedSlots) ? OverlapsBookingReason : null);

                // Bước 2: slot nên có mà chưa có → tạo mới.
                if (!existingByKey.TryGetValue((desired.DoctorId, desired.Date, desired.StartTime), out var existing))
                {
                    newSlots.Add(CreateSlot(desired, unavailableReason, changedAt));
                    continue;
                }

                // Bước 3: slot đã có (và chưa ai đặt) → cập nhật theo lịch mới.
                if (existing.Status == AppointmentSlotStatus.Booked)
                    continue;
                hasChanges |= UpdateFromSchedule(existing, desired, changedAt);
                hasChanges |= SetAvailability(existing, unavailableReason, changedAt);
            }

            // Bước 4: slot đang có nhưng không còn trong lịch làm việc → khóa lại (không xóa, để giữ lịch sử).
            foreach (var existing in existingSlots)
            {
                if (existing.Status != AppointmentSlotStatus.Booked &&
                    !desiredSlots.ContainsKey((existing.DoctorId, existing.Date, existing.StartTime)))
                    hasChanges |= SetAvailability(existing, RemovedFromScheduleReason, changedAt);
            }

            if (newSlots.Count > 0)
            {
                await _appointmentSlotRepository.AddRangeAsync(newSlots);
                hasChanges = true;
            }

            if (hasChanges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _appointmentSlotRepository.SaveChangesAsync();
            }
        }, cancellationToken);

    // Cắt mỗi ca làm việc thành các slot 30 phút cho từng ngày trong khoảng.
    private static Dictionary<(int DoctorId, DateOnly Date, TimeOnly StartTime), DesiredSlot> BuildDesiredSlots(
        List<DoctorShift> schedules, DateOnly fromDate, DateOnly toDate)
    {
        var desiredSlots = new Dictionary<(int, DateOnly, TimeOnly), DesiredSlot>();
        // Các slot đã lấy của từng bác sĩ trong từng ngày, để kiểm tra chồng giờ chỉ trong phạm vi nhỏ đó.
        var takenByDoctorAndDate = new Dictionary<(int DoctorId, DateOnly Date), List<DesiredSlot>>();

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var schedulesOfDay = schedules
                .Where(schedule => schedule.Shift.DayOfWeek == date.DayOfWeek)
                .OrderBy(schedule => schedule.DoctorId)
                .ThenBy(schedule => schedule.Shift.StartTime);

            foreach (var schedule in schedulesOfDay)
            {
                if (!ClinicSchedule.IsValidShiftDuration(schedule.Shift.StartTime, schedule.Shift.EndTime))
                    continue;

                if (!takenByDoctorAndDate.TryGetValue((schedule.DoctorId, date), out var taken))
                {
                    taken = [];
                    takenByDoctorAndDate.Add((schedule.DoctorId, date), taken);
                }

                for (var start = schedule.Shift.StartTime; start < schedule.Shift.EndTime;
                     start = start.AddMinutes(ClinicSchedule.SlotMinutes))
                {
                    var candidate = new DesiredSlot(schedule.DoctorId, schedule.DoctorShiftId, date,
                        start, start.AddMinutes(ClinicSchedule.SlotMinutes));

                    // Dữ liệu cũ có thể có hai ca chồng nhau cho cùng bác sĩ: ca nào đến trước thì giữ, không tạo slot trùng giờ.
                    if (taken.Any(slot => slot.StartTime < candidate.EndTime && candidate.StartTime < slot.EndTime))
                        continue;

                    taken.Add(candidate);
                    desiredSlots.Add((candidate.DoctorId, candidate.Date, candidate.StartTime), candidate);
                }
            }
        }

        return desiredSlots;
    }

    // Bệnh viện nghỉ được ưu tiên hơn bác sĩ nghỉ. Trả null nếu hôm đó làm việc bình thường.
    private static string? GetDayOffReason(List<NonWorkingDay> nonWorkingDays, int doctorId, DateOnly date)
    {
        var hospitalDayOff = nonWorkingDays.FirstOrDefault(day => day.DoctorId == null && day.Date == date);
        if (hospitalDayOff is not null)
            return string.IsNullOrWhiteSpace(hospitalDayOff.Reason) ? "Bệnh viện nghỉ." : hospitalDayOff.Reason;

        var doctorDayOff = nonWorkingDays.FirstOrDefault(day => day.DoctorId == doctorId && day.Date == date);
        if (doctorDayOff is not null)
            return string.IsNullOrWhiteSpace(doctorDayOff.Reason) ? "Bác sĩ nghỉ." : doctorDayOff.Reason;

        return null;
    }

    // Slot mới có bị chồng giờ với một slot đã được đặt (khác giờ bắt đầu) của cùng bác sĩ hay không.
    // Xảy ra khi đổi giờ ca làm việc sau khi đã có người đặt lịch theo giờ cũ.
    private static bool OverlapsBooking(DesiredSlot desired, List<AppointmentSlot> bookedSlots)
        => bookedSlots.Any(booked =>
            booked.DoctorId == desired.DoctorId && booked.Date == desired.Date &&
            booked.StartTime != desired.StartTime &&
            booked.StartTime < desired.EndTime && desired.StartTime < booked.EndTime);

    private static AppointmentSlot CreateSlot(DesiredSlot desired, string? unavailableReason, DateTime createdAt) => new()
    {
        DoctorId = desired.DoctorId,
        DoctorShiftId = desired.DoctorShiftId,
        Date = desired.Date,
        StartTime = desired.StartTime,
        EndTime = desired.EndTime,
        Status = unavailableReason is null ? AppointmentSlotStatus.Available : AppointmentSlotStatus.Unavailable,
        UnavailableReason = unavailableReason,
        CreatedAt = createdAt
    };

    // Trả true nếu có thay đổi (để biết có cần SaveChanges hay không).
    private static bool UpdateFromSchedule(AppointmentSlot slot, DesiredSlot desired, DateTime changedAt)
    {
        if (slot.DoctorShiftId == desired.DoctorShiftId && slot.EndTime == desired.EndTime)
            return false;

        slot.DoctorShiftId = desired.DoctorShiftId;
        slot.EndTime = desired.EndTime;
        slot.UpdatedAt = changedAt;
        return true;
    }

    // Trả true nếu có thay đổi (để biết có cần SaveChanges hay không).
    private static bool SetAvailability(AppointmentSlot slot, string? unavailableReason, DateTime changedAt)
    {
        var status = unavailableReason is null ? AppointmentSlotStatus.Available : AppointmentSlotStatus.Unavailable;
        if (slot.Status == status && slot.UnavailableReason == unavailableReason)
            return false;

        slot.Status = status;
        slot.UnavailableReason = unavailableReason;
        slot.UpdatedAt = changedAt;
        return true;
    }

    private static AppointmentSlotDto MapToDto(AppointmentSlot slot) => new()
    {
        AppointmentSlotId = slot.AppointmentSlotId,
        DoctorId = slot.DoctorId,
        DoctorShiftId = slot.DoctorShiftId,
        Date = slot.Date,
        StartTime = slot.StartTime,
        EndTime = slot.EndTime,
        Status = slot.Status,
        UnavailableReason = slot.UnavailableReason
    };

    private sealed record DesiredSlot(int DoctorId, int DoctorShiftId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);
}
