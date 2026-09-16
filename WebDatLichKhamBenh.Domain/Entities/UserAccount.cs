using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Domain.Entities;

public class UserAccount
{
    public Guid UserAccountId { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public AccountRole Role { get; set; }
    public int? PatientId { get; set; }
    public Patient? Patient { get; set; }
    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
