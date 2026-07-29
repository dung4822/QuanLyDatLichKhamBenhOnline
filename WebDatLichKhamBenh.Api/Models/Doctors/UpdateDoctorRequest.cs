using Microsoft.AspNetCore.Http;

namespace WebDatLichKhamBenh.Api.Models.Doctors;

public class UpdateDoctorRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public int Gender { get; set; }
    public string? Address { get; set; }
    public DateOnly CareerStartDate { get; set; }
    public int SpecialtyId { get; set; }
    public IFormFile? Avatar { get; set; }
    public bool RemoveAvatar { get; set; }
}
