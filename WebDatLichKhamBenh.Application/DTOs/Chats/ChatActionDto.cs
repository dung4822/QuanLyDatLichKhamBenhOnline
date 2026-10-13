using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.DTOs.Chats;

// Thẻ xác nhận hiển thị cho người dùng. UI chỉ cần vẽ Title + Details và hai nút Xác nhận / Hủy.
public record ChatActionDto(
    Guid ChatActionId,
    ChatActionType Type,
    ChatActionStatus Status,
    string Title,
    IReadOnlyList<ChatActionDetail> Details,
    DateTime ExpiresAt,
    DateTime? CompletedAt,
    string? FailureReason);

// Một dòng trên thẻ. Before chỉ có khi đổi lịch/sửa hồ sơ (giá trị cũ → giá trị mới).
public record ChatActionDetail(string Label, string? Before, string Value);

// Phần nội dung thẻ được lưu vào cột PreviewJson.
public record ChatActionPreview(string Title, IReadOnlyList<ChatActionDetail> Details);
