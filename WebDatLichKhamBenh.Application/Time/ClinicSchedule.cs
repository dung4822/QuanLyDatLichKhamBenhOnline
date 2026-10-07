namespace WebDatLichKhamBenh.Application.Time;

// Các quy tắc lịch khám của phòng khám, đặt ở một chỗ để đổi một lần là áp dụng mọi nơi.
public static class ClinicSchedule
{
    // Mỗi khung giờ khám dài 30 phút.
    public const int SlotMinutes = 30;

    // Chỉ mở đặt lịch trong 7 ngày tính cả hôm nay.
    public const int BookingWindowDays = 7;

    // Đổi lịch phải trước giờ khám ít nhất 2 tiếng.
    public static readonly TimeSpan RescheduleDeadline = TimeSpan.FromHours(2);

    public static DateOnly LastBookableDate => ClinicClock.Today.AddDays(BookingWindowDays - 1);

    // Ca làm việc phải dài ít nhất một khung và chia hết thành các khung 30 phút.
    public static bool IsValidShiftDuration(TimeOnly startTime, TimeOnly endTime)
    {
        var minutes = (endTime - startTime).TotalMinutes;
        return minutes >= SlotMinutes && minutes % SlotMinutes == 0;
    }
}
