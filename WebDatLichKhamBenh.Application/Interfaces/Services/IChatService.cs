using WebDatLichKhamBenh.Application.DTOs.Chats;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IChatService
{
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
