namespace WebDatLichKhamBenh.Application.DTOs.Shifts;

public record UpdateShiftRequest
{
    public DayOfWeek DayOfWeek { get; init; }
    public string Name { get; init; } = string.Empty;
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public bool IsActive { get; init; }
}
