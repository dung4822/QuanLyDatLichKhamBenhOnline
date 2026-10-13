using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/chats")]
[Authorize(Roles = "Patient")]
public class ChatsController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IChatActionService _actionService;

    public ChatsController(
        IChatService chatService,
        IChatActionService actionService)
    {
        _chatService = chatService;
        _actionService = actionService;
    }

    [HttpPost("sessions")]
    [ProducesResponseType(typeof(ChatSessionDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSession()
    {
        var session = await _chatService.CreateSessionAsync();
        return CreatedAtAction(nameof(GetSession), new { sessionId = session.ChatSessionId }, session);
    }

    [HttpGet("sessions")]
    [ProducesResponseType(typeof(List<ChatSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions() => Ok(await _chatService.GetSessionsAsync());

    // Dùng khi reload trang: lấy lại các lượt chat và trạng thái thẻ xác nhận.
    [HttpGet("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(ChatSessionDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSession(Guid sessionId) => Ok(await _chatService.GetSessionAsync(sessionId));

    // Chỉ endpoint này gọi AI, nên chỉ endpoint này bị giới hạn 10 request/phút.
    [HttpPost("sessions/{sessionId:guid}/messages")]
    [EnableRateLimiting("chat")]
    [RequestSizeLimit(65536)]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendMessage(Guid sessionId, SendChatMessageRequest request,
        CancellationToken cancellationToken)
        => Ok(await _chatService.SendAsync(sessionId, request, cancellationToken));

    [HttpPost("actions/{actionId:guid}/confirm")]
    [ProducesResponseType(typeof(ChatActionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmAction(Guid actionId, CancellationToken cancellationToken)
        => Ok(await _actionService.ConfirmAsync(actionId, cancellationToken));

    [HttpPost("actions/{actionId:guid}/reject")]
    [ProducesResponseType(typeof(ChatActionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectAction(Guid actionId) => Ok(await _actionService.RejectAsync(actionId));
}
