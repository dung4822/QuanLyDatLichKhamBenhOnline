namespace WebDatLichKhamBenh.Application.DTOs.Chats;

// Quy tắc hợp lệ nằm ở Validators/ChatRequestValidator.cs.
public class ChatRequest
{
    public string Message { get; set; } = string.Empty;

    // Lịch sử do client giữ và gửi lại mỗi lượt; server không lưu cuộc trò chuyện.
    public List<ChatHistoryMessage> History { get; set; } = [];
}

public class ChatHistoryMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
