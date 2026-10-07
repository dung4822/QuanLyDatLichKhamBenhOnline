using WebDatLichKhamBenh.Application.DTOs.Chats;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface ILlmClient
{
    Task<LlmReply> CompleteAsync(IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools, CancellationToken cancellationToken = default);
}
