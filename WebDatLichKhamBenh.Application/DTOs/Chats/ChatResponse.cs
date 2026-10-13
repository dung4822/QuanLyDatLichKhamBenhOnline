namespace WebDatLichKhamBenh.Application.DTOs.Chats;

public record ChatResponse(Guid ChatSessionId, long ChatTurnId, string Reply)
{
    // Thẻ xác nhận nếu trợ lý vừa đề xuất đặt/đổi/hủy lịch hoặc sửa hồ sơ.
    public ChatActionDto? Action { get; init; }

    // Dữ liệu backend tra cứu trong lượt này (không lưu; xem lại cuộc trò chuyện sẽ không có).
    public IReadOnlyList<ChatLookupResult> Sources { get; init; } = [];
}
