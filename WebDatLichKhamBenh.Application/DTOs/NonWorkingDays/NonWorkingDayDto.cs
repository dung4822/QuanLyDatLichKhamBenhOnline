namespace WebDatLichKhamBenh.Application.DTOs.NonWorkingDays;

public record NonWorkingDayDto
{
    public int NonWorkingDayId { get; init; }
    public DateOnly Date { get; init; }
    public int? DoctorId { get; init; }
    public string? DoctorFullName { get; init; }
    public string? Reason { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
