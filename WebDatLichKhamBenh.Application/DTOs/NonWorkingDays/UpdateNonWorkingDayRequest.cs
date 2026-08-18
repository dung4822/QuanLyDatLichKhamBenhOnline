namespace WebDatLichKhamBenh.Application.DTOs.NonWorkingDays;

public record UpdateNonWorkingDayRequest
{
    public DateOnly Date { get; init; }
    public int? DoctorId { get; init; }
    public string? Reason { get; init; }
}
