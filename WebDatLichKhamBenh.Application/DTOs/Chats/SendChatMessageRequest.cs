namespace WebDatLichKhamBenh.Application.DTOs.Chats;

// Quy tắc hợp lệ nằm ở Validators/SendChatMessageValidator.cs.
public class SendChatMessageRequest
{
    public string Message { get; set; } = string.Empty;

    // Client tự tạo một Guid cho mỗi tin nhắn và dùng lại đúng Guid đó khi gửi lại sau lỗi mạng.
    // Bỏ trống thì server tự tạo (khi đó gửi lại sẽ được xem là tin nhắn mới).
    public Guid? ClientMessageId { get; set; }
}
