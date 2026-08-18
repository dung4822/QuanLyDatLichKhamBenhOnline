namespace WebDatLichKhamBenh.Application.Time;

public static class ClinicClock
{
    private const string IanaTimeZoneId = "Asia/Ho_Chi_Minh";
    private const string WindowsTimeZoneId = "SE Asia Standard Time";

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    private static TimeZoneInfo TimeZone { get; } = ResolveTimeZone();

    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(IanaTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsTimeZoneId);
        }
    }
}
