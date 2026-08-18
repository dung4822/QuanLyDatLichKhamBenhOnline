namespace WebDatLichKhamBenh.Application.DTOs.Patients;

public record PatientDto
{
    public int PatientId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public DateOnly DateOfBirth { get; init; }
    public int Gender { get; init; }
    public string PhoneNumber { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Address { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
