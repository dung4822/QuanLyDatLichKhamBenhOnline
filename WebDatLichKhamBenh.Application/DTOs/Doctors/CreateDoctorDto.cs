using WebDatLichKhamBenh.Application.DTOs.Images;

namespace WebDatLichKhamBenh.Application.DTOs.Doctors;

public record CreateDoctorDto
{
    public string FullName { get; init; } = default!;
    public string? PhoneNumber { get; init; }
    public string? Email { get; init; }
    public int Gender { get; init; }
    public string? Address { get; init; }
    public DateOnly CareerStartDate { get; init; }
    public int SpecialtyId { get; init; }
    public ImageUploadDto? Avatar { get; init; }
}
