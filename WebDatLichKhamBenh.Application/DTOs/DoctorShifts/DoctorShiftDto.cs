namespace WebDatLichKhamBenh.Application.DTOs.DoctorShifts;

public record DoctorShiftDto
{
    public int DoctorShiftId { get; init; }
    public int DoctorId { get; init; }
    public string DoctorFullName { get; init; } = string.Empty;
    public int ShiftId { get; init; }
    public string ShiftName { get; init; } = string.Empty;
    public DayOfWeek DayOfWeek { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
