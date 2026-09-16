namespace WebDatLichKhamBenh.Domain.Entities;

public class AppointmentHistory
{
    public long AppointmentHistoryId { get; set; }
    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    public Guid ActorAccountId { get; set; }
    public UserAccount ActorAccount { get; set; } = null!;
    public string ActorDisplayName { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
