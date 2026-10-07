using WebDatLichKhamBenh.Application.DTOs.Chats;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IChatLookupService
{
    IReadOnlyList<LlmToolDefinition> Tools { get; }
    Task<ChatLookupResult> ExecuteAsync(LlmToolCall toolCall, CancellationToken cancellationToken = default);
}
