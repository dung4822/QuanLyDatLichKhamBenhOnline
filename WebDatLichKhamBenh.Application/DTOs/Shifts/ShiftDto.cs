namespace WebDatLichKhamBenh.Application.DTOs.Shifts;

public record ShiftDto
{
    public int ShiftId { get; init; }
    public DayOfWeek DayOfWeek { get; init; }
    public string Name { get; init; } = string.Empty;
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
