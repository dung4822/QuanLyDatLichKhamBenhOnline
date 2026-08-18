using FluentValidation;
using FluentValidation.Results;
using WebDatLichKhamBenh.Application.DTOs.AppointmentSlots;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Services;

public class AppointmentSlotService : IAppointmentSlotService
{
    private const int SlotMinutes = 30;
    private const int RollingWindowDays = 7;
    private readonly IAppointmentSlotRepository _appointmentSlotRepository;
    private readonly IDoctorRepository _doctorRepository;

    public AppointmentSlotService(
        IAppointmentSlotRepository appointmentSlotRepository,
        IDoctorRepository doctorRepository)
    {
        _appointmentSlotRepository = appointmentSlotRepository;
        _doctorRepository = doctorRepository;
    }

    public async Task<List<AppointmentSlotDto>?> GetAvailableSlotsAsync(
        int doctorId,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        if (await _doctorRepository.GetDoctorByIdAsync(doctorId) is null)
        {
            return null;
        }

        var (from, to) = GetValidatedReadRange(fromDate, toDate);
        var slots = await _appointmentSlotRepository.GetAvailableSlotsAsync(doctorId, from, to);
        var now = ClinicClock.Now;
        var today = DateOnly.FromDateTime(now);
        var currentTime = TimeOnly.FromDateTime(now);

        return slots
            .Where(slot => slot.Date > today ||
                           (slot.Date == today && slot.StartTime > currentTime))
            .Select(MapToDto)
            .ToList();
    }

    public Task EnsureRollingWindowAsync(CancellationToken cancellationToken = default)
    {
        var today = ClinicClock.Today;
        return SyncRangeAsync(today, today.AddDays(RollingWindowDays - 1), null, cancellationToken);
    }

    public async Task SyncRangeAsync(
        DateOnly fromDate,
        DateOnly toDate,
        int? doctorId = null,
        CancellationToken cancellationToken = default)
    {
        if (toDate < fromDate)
        {
            throw new ArgumentException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.", nameof(toDate));
        }

        var (from, to, hasWindow) = IntersectWithRollingWindow(fromDate, toDate);
        if (!hasWindow)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Các repository dùng chung một scoped DbContext. EF Core không hỗ trợ nhiều
        // truy vấn song song trên cùng context, nên giữ ba truy vấn bulk này tuần tự.
        var schedules = await _appointmentSlotRepository.GetActiveDoctorShiftsAsync(doctorId);
        var nonWorkingDays = await _appointmentSlotRepository.GetNonWorkingDaysAsync(from, to);
        var existingSlots = await _appointmentSlotRepository.GetSlotsTrackingAsync(from, to, doctorId);

        var desiredSlots = BuildDesiredSlots(schedules, from, to);
        var (hospitalReasons, doctorReasons) = BuildUnavailableReasons(nonWorkingDays);
        var existingByKey = existingSlots.ToDictionary(slot => (slot.DoctorId, slot.Date, slot.StartTime));
        var createdSlots = new List<AppointmentSlot>();
        var changedAt = DateTime.UtcNow;

        var hasChanges = false;
        foreach (var desiredSlot in desiredSlots.Values)
        {
            var key = (desiredSlot.DoctorId, desiredSlot.Date, desiredSlot.StartTime);
            var unavailableReason = GetUnavailableReason(
                hospitalReasons,
                doctorReasons,
                desiredSlot.DoctorId,
                desiredSlot.Date);

            if (!existingByKey.TryGetValue(key, out var existingSlot))
            {
                createdSlots.Add(new AppointmentSlot
                {
                    DoctorId = desiredSlot.DoctorId,
                    DoctorShiftId = desiredSlot.DoctorShiftId,
                    Date = desiredSlot.Date,
                    StartTime = desiredSlot.StartTime,
                    EndTime = desiredSlot.EndTime,
                    Status = unavailableReason is null
                        ? AppointmentSlotStatus.Available
                        : AppointmentSlotStatus.Unavailable,
                    UnavailableReason = unavailableReason,
                    CreatedAt = changedAt
                });
                hasChanges = true;
                continue;
            }

            if (existingSlot.Status == AppointmentSlotStatus.Booked)
            {
                continue;
            }

            if (existingSlot.DoctorShiftId != desiredSlot.DoctorShiftId || existingSlot.EndTime != desiredSlot.EndTime)
            {
                existingSlot.DoctorShiftId = desiredSlot.DoctorShiftId;
                existingSlot.EndTime = desiredSlot.EndTime;
                existingSlot.UpdatedAt = changedAt;
                hasChanges = true;
            }

            hasChanges |= SetAvailability(existingSlot, unavailableReason, changedAt);
        }

        foreach (var existingSlot in existingSlots)
        {
            if (existingSlot.Status == AppointmentSlotStatus.Booked ||
                desiredSlots.ContainsKey((existingSlot.DoctorId, existingSlot.Date, existingSlot.StartTime)))
            {
                continue;
            }

            hasChanges |= SetAvailability(existingSlot, "Không còn trong lịch làm việc.", changedAt);
        }

        if (createdSlots.Count > 0)
        {
            await _appointmentSlotRepository.AddRangeAsync(createdSlots);
        }

        if (hasChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _appointmentSlotRepository.SaveChangesAsync();
        }
    }

    private static Dictionary<(int DoctorId, DateOnly Date, TimeOnly StartTime), DesiredSlot> BuildDesiredSlots(
        IEnumerable<DoctorShift> schedules,
        DateOnly fromDate,
        DateOnly toDate)
    {
        var desiredSlots = new Dictionary<(int, DateOnly, TimeOnly), DesiredSlot>();
        var occupiedRanges = new Dictionary<(int DoctorId, DateOnly Date), List<DesiredSlot>>();

        foreach (var date in EnumerateDates(fromDate, toDate))
        {
            foreach (var schedule in schedules
                         .Where(schedule => schedule.Shift.DayOfWeek == date.DayOfWeek)
                         .OrderBy(schedule => schedule.DoctorId)
                         .ThenBy(schedule => schedule.Shift.StartTime))
            {
                var durationMinutes = (schedule.Shift.EndTime - schedule.Shift.StartTime).TotalMinutes;
                if (durationMinutes < SlotMinutes || durationMinutes % SlotMinutes != 0)
                {
                    continue;
                }

                var rangeKey = (schedule.DoctorId, date);
                if (!occupiedRanges.TryGetValue(rangeKey, out var ranges))
                {
                    ranges = [];
                    occupiedRanges.Add(rangeKey, ranges);
                }

                for (var start = schedule.Shift.StartTime; start < schedule.Shift.EndTime; start = start.AddMinutes(SlotMinutes))
                {
                    var candidate = new DesiredSlot(
                        schedule.DoctorId,
                        schedule.DoctorShiftId,
                        date,
                        start,
                        start.AddMinutes(SlotMinutes));

                    // Dữ liệu legacy có thể có ca chồng nhau. Không tạo slot trùng giờ dù cấu hình đó tồn tại.
                    if (ranges.Any(range => range.StartTime < candidate.EndTime && candidate.StartTime < range.EndTime))
                    {
                        continue;
                    }

                    ranges.Add(candidate);
                    desiredSlots.Add((candidate.DoctorId, candidate.Date, candidate.StartTime), candidate);
                }
            }
        }

        return desiredSlots;
    }

    private static (Dictionary<DateOnly, string?> HospitalReasons, Dictionary<(int DoctorId, DateOnly Date), string?> DoctorReasons)
        BuildUnavailableReasons(IEnumerable<NonWorkingDay> nonWorkingDays)
    {
        var hospitalReasons = nonWorkingDays
            .Where(day => day.DoctorId is null)
            .GroupBy(day => day.Date)
            .ToDictionary(group => group.Key, group => group.First().Reason);

        var doctorReasons = nonWorkingDays
            .Where(day => day.DoctorId.HasValue)
            .GroupBy(day => (day.DoctorId!.Value, day.Date))
            .ToDictionary(group => group.Key, group => group.First().Reason);

        return (hospitalReasons, doctorReasons);
    }

    private static string? GetUnavailableReason(
        IReadOnlyDictionary<DateOnly, string?> hospitalReasons,
        IReadOnlyDictionary<(int DoctorId, DateOnly Date), string?> doctorReasons,
        int doctorId,
        DateOnly date)
    {
        return hospitalReasons.TryGetValue(date, out var hospitalReason)
            ? string.IsNullOrWhiteSpace(hospitalReason) ? "Bệnh viện nghỉ." : hospitalReason
            : doctorReasons.TryGetValue((doctorId, date), out var doctorReason)
                ? string.IsNullOrWhiteSpace(doctorReason) ? "Bác sĩ nghỉ." : doctorReason
                : null;
    }

    private static bool SetAvailability(AppointmentSlot appointmentSlot, string? unavailableReason, DateTime changedAt)
    {
        var status = unavailableReason is null
            ? AppointmentSlotStatus.Available
            : AppointmentSlotStatus.Unavailable;

        if (appointmentSlot.Status == status && appointmentSlot.UnavailableReason == unavailableReason)
        {
            return false;
        }

        appointmentSlot.Status = status;
        appointmentSlot.UnavailableReason = unavailableReason;
        appointmentSlot.UpdatedAt = changedAt;
        return true;
    }

    private static IEnumerable<DateOnly> EnumerateDates(DateOnly fromDate, DateOnly toDate)
    {
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static (DateOnly From, DateOnly To) GetValidatedReadRange(DateOnly? fromDate, DateOnly? toDate)
    {
        var today = ClinicClock.Today;
        var windowEnd = today.AddDays(RollingWindowDays - 1);
        var from = fromDate ?? today;
        var to = toDate ?? windowEnd;

        if (from < today || to > windowEnd || to < from || from.DayNumber + RollingWindowDays - 1 < to.DayNumber)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("dateRange", "Khoảng ngày phải nằm trong 7 ngày từ hôm nay theo giờ Việt Nam.")
            });
        }

        return (from, to);
    }

    private static (DateOnly From, DateOnly To, bool HasWindow) IntersectWithRollingWindow(DateOnly fromDate, DateOnly toDate)
    {
        var windowStart = ClinicClock.Today;
        var windowEnd = windowStart.AddDays(RollingWindowDays - 1);
        var from = fromDate > windowStart ? fromDate : windowStart;
        var to = toDate < windowEnd ? toDate : windowEnd;
        return (from, to, from <= to);
    }

    private static AppointmentSlotDto MapToDto(AppointmentSlot appointmentSlot)
    {
        return new AppointmentSlotDto
        {
            AppointmentSlotId = appointmentSlot.AppointmentSlotId,
            DoctorId = appointmentSlot.DoctorId,
            DoctorShiftId = appointmentSlot.DoctorShiftId,
            Date = appointmentSlot.Date,
            StartTime = appointmentSlot.StartTime,
            EndTime = appointmentSlot.EndTime,
            Status = appointmentSlot.Status,
            UnavailableReason = appointmentSlot.UnavailableReason
        };
    }

    private sealed record DesiredSlot(
        int DoctorId,
        int DoctorShiftId,
        DateOnly Date,
        TimeOnly StartTime,
        TimeOnly EndTime);
}
