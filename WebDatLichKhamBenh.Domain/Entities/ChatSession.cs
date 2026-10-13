namespace WebDatLichKhamBenh.Domain.Entities;

// Một cuộc trò chuyện của bệnh nhân với trợ lý AI. Lưu ở server để reload trang không mất chat.
public class ChatSession
{
    public Guid ChatSessionId { get; set; } = Guid.NewGuid();
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatTurn> Turns { get; set; } = [];
}
