namespace WebDatLichKhamBenh.Application.DTOs.Chats;

public record ChatResponse(string Reply)
{
    public IReadOnlyList<ChatLookupResult> Sources { get; init; } = [];
    public IReadOnlyList<ChatHistoryMessage> History { get; init; } = [];
}
