namespace WebDatLichKhamBenh.Domain.Entities;

public class UserSession
{
    public Guid UserSessionId { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public UserAccount UserAccount { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
