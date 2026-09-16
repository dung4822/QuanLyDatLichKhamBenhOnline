using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController(IAccountService accounts) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request)
        => StatusCode(201, await accounts.RegisterAsync(request));

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await accounts.LoginAsync(request));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout() { await accounts.LogoutAsync(); return NoContent(); }

    [HttpPost("change-password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    { await accounts.ChangePasswordAsync(request); return NoContent(); }
}
