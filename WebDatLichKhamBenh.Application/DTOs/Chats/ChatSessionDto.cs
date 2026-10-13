namespace WebDatLichKhamBenh.Application.DTOs.Chats;

public record ChatSessionDto(Guid ChatSessionId, string Title, DateTime CreatedAt, DateTime UpdatedAt);

public record ChatTurnDto(long ChatTurnId, string Message, string Reply, DateTime CreatedAt, ChatActionDto? Action);

public record ChatSessionDetailDto(ChatSessionDto Session, IReadOnlyList<ChatTurnDto> Turns);
