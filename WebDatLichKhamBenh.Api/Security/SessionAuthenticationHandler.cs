using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Security;

public class SessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IAccountService accounts)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ClinicBearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var session = await accounts.AuthenticateAsync(header[7..].Trim());
        if (session is null) return AuthenticateResult.Fail("Phiên đăng nhập không hợp lệ hoặc đã hết hạn.");
        var account = session.UserAccount;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.UserAccountId.ToString()),
            new(ClaimTypes.Name, account.DisplayName), new(ClaimTypes.Role, account.Role.ToString()),
            new("session_id", session.UserSessionId.ToString())
        };
        if (account.PatientId.HasValue) claims.Add(new("patient_id", account.PatientId.Value.ToString()));
        if (account.DoctorId.HasValue) claims.Add(new("doctor_id", account.DoctorId.Value.ToString()));
        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem(statusCode: 401, title: "Vui lòng đăng nhập.").ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => Results.Problem(statusCode: 403, title: "Bạn không có quyền thực hiện thao tác này.").ExecuteAsync(Context);
}
