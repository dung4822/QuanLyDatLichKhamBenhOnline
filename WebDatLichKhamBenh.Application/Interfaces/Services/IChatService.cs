using WebDatLichKhamBenh.Application.DTOs.Chats;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IChatService
{
    Task<ChatSessionDto> CreateSessionAsync();
    Task<List<ChatSessionDto>> GetSessionsAsync();
    Task<ChatSessionDetailDto> GetSessionAsync(Guid sessionId);
    Task<ChatResponse> SendAsync(Guid sessionId, SendChatMessageRequest request, CancellationToken cancellationToken = default);
}
