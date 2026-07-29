using System.ComponentModel.DataAnnotations;

namespace WebDatLichKhamBenh.Application.DTOs.Specialties;

public class UpdateSpecialtyDto
{
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(100)]
    public string? Description { get; set; }
    [MaxLength(20)]
    public string Status { get; set; } = string.Empty;
}
