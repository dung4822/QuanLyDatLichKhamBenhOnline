namespace WebDatLichKhamBenh.Domain.Entities;

public class UserSession
{
    public Guid UserSessionId { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public UserAccount UserAccount { get; set; } = null!;
    // Chỉ lưu SHA-256 của token ngẫu nhiên, không lưu bearer token gốc.
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
