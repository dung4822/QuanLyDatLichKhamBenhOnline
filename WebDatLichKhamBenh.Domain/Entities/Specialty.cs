namespace WebDatLichKhamBenh.Domain.Entities;

public class Specialty
{
    public int SpecialtyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Doctor> Doctors { get; set; }
}
