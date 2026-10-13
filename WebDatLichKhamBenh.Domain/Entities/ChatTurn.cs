namespace WebDatLichKhamBenh.Domain.Entities;

// Một lượt chat = câu hỏi của người dùng + câu trả lời của trợ lý, lưu cùng lúc.
// Nhờ lưu theo cặp, lịch sử luôn đủ cặp user/assistant và không có câu hỏi "treo" thiếu câu trả lời.
public class ChatTurn
{
    public long ChatTurnId { get; set; }
    public Guid ChatSessionId { get; set; }
    public ChatSession ChatSession { get; set; } = null!;

    // Mã do client tạo cho mỗi tin nhắn. Gửi lại cùng mã (do mất mạng) sẽ nhận lại câu trả lời cũ,
    // không gọi AI thêm lần nữa và không tạo đề xuất trùng.
    public Guid ClientMessageId { get; set; }

    public string UserMessage { get; set; } = string.Empty;
    public string AssistantReply { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Đề xuất thao tác (nếu có) mà trợ lý tạo ra trong lượt này.
    public ChatAction? Action { get; set; }
}
