using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/chats")]
[Authorize(Roles = "Patient")]
[EnableRateLimiting("chat")]
[RequestSizeLimit(65536)]
public class ChatsController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IWebHostEnvironment _environment;

    public ChatsController(
        IChatService chatService,
        IWebHostEnvironment environment)
    {
        _chatService = chatService;
        _environment = environment;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendAsync(ChatRequest request, CancellationToken cancellationToken)
        => Ok(await _chatService.SendAsync(request, cancellationToken));

    [HttpPost("test")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        // Endpoint học thử: không mở gọi LLM miễn đăng nhập trong môi trường triển khai.
        if (!_environment.IsDevelopment()) return NotFound();
        return Ok(await _chatService.SendAsync(request, cancellationToken));
    }
}
