using System.ComponentModel.DataAnnotations;

namespace WebDatLichKhamBenh.Application.DTOs.Specialties;

public class CreateSpecialtyDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = string.Empty;
}
