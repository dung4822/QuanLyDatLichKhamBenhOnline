using System.Text.Json;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Mappings;

public static class ChatMappings
{
    public static ChatSessionDto ToDto(this ChatSession session)
        => new(session.ChatSessionId, session.Title, session.CreatedAt, session.UpdatedAt);

    public static ChatTurnDto ToDto(this ChatTurn turn)
        => new(turn.ChatTurnId, turn.UserMessage, turn.AssistantReply, turn.CreatedAt, turn.Action?.ToDto());

    public static ChatActionDto ToDto(this ChatAction action)
    {
        var preview = JsonSerializer.Deserialize<ChatActionPreview>(action.PreviewJson)!;
        return new ChatActionDto(action.ChatActionId, action.Type, action.GetCurrentStatus(),
            preview.Title, preview.Details, action.ExpiresAt, action.CompletedAt, action.FailureReason);
    }

    // Đề xuất quá hạn có thể vẫn lưu Pending (chưa ai bấm), nhưng phải hiển thị là Expired.
    public static ChatActionStatus GetCurrentStatus(this ChatAction action)
        => action.Status == ChatActionStatus.Pending && action.ExpiresAt <= DateTime.UtcNow
            ? ChatActionStatus.Expired
            : action.Status;
}
