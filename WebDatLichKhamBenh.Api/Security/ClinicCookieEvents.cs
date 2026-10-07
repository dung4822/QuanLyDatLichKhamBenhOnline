using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using WebDatLichKhamBenh.Api.Extensions;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Security;

public class ClinicCookieEvents : CookieAuthenticationEvents
{
    private readonly IAuthService _authService;

    public ClinicCookieEvents(IAuthService authService)
    {
        _authService = authService;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var accountClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var sessionClaim = context.Principal?.FindFirstValue(ClinicClaims.SessionId);

        if (Guid.TryParse(accountClaim, out var accountId) && Guid.TryParse(sessionClaim, out var sessionId))
        {
            var session = await _authService.ValidateSessionAsync(sessionId, accountId);
            if (session is not null)
            {
                // Dùng quyền và liên kết hiện tại trong database cho request này.
                context.ReplacePrincipal(ClinicClaims.CreatePrincipal(session));
                return;
            }
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // Web API trả mã lỗi JSON, không redirect sang trang đăng nhập như ứng dụng MVC.
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        return context.HttpContext.WriteProblemAsync(StatusCodes.Status401Unauthorized);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        return context.HttpContext.WriteProblemAsync(StatusCodes.Status403Forbidden,
            "Bạn không có quyền thực hiện thao tác này.");
    }
}
