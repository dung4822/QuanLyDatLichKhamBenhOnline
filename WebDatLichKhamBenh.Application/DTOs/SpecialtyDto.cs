namespace WebDatLichKhamBenh.Application.DTOs;

public class SpecialtyDto
{
    public int SpecialtyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
