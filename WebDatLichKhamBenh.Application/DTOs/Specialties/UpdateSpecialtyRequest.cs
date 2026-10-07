namespace WebDatLichKhamBenh.Application.DTOs.Specialties;

public class UpdateSpecialtyRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
}
