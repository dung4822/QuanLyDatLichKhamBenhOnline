using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Domain.Entities;

// Đề xuất thao tác do trợ lý AI tạo. AI KHÔNG tự ghi dữ liệu: chỉ khi người dùng bấm Xác nhận
// thì backend mới gọi service nghiệp vụ để đặt/đổi/hủy lịch hoặc sửa hồ sơ.
public class ChatAction
{
    public Guid ChatActionId { get; set; } = Guid.NewGuid();
    public long ChatTurnId { get; set; }
    public ChatTurn ChatTurn { get; set; } = null!;
    public ChatActionType Type { get; set; }
    public ChatActionStatus Status { get; set; } = ChatActionStatus.Pending;

    // Tham số thật để thực thi (ID slot, ID lịch hẹn...), dạng JSON. Không phụ thuộc trí nhớ của AI.
    public string PayloadJson { get; set; } = string.Empty;

    // Nội dung thẻ xác nhận cho người dùng xem, lấy từ database lúc tạo đề xuất.
    public string PreviewJson { get; set; } = string.Empty;

    public string? ResultJson { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
