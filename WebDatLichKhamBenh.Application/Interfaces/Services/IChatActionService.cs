using WebDatLichKhamBenh.Application.DTOs.Chats;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IChatActionService
{
    // Công cụ dành cho mô hình: xem lịch của tôi + tạo đề xuất đặt/hủy/đổi lịch, sửa hồ sơ.
    IReadOnlyList<LlmToolDefinition> Tools { get; }
    bool CanHandle(string toolName);
    Task<ChatToolResult> ExecuteAsync(LlmToolCall toolCall, CancellationToken cancellationToken = default);

    // Do người dùng bấm nút trên thẻ, không đi qua mô hình.
    Task<ChatActionDto> ConfirmAsync(Guid actionId, CancellationToken cancellationToken = default);
    Task<ChatActionDto> RejectAsync(Guid actionId);
}
