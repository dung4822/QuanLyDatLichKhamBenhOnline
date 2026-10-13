namespace WebDatLichKhamBenh.Application.Exceptions;

// Đề xuất của trợ lý không còn xác nhận được (đã hết hạn, đã hủy, hồ sơ đã đổi...). Trả về HTTP 409.
public class ChatActionConflictException : Exception
{
    public ChatActionConflictException(string message) : base(message) { }
}
