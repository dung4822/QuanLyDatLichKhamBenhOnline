using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using WebDatLichKhamBenh.Application.DTOs.Accounts;

namespace WebDatLichKhamBenh.Api.Security;

public static class ClinicClaims
{
    public const string SessionId = "session_id";
    public const string PatientId = "patient_id";
    public const string DoctorId = "doctor_id";

    public static ClaimsPrincipal CreatePrincipal(AuthSessionDto session)
    {
        var account = session.Account;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.UserAccountId.ToString()),
            new(ClaimTypes.Name, account.DisplayName),
            new(ClaimTypes.Role, account.Role.ToString()),
            new(SessionId, session.SessionId.ToString())
        };

        if (account.PatientId.HasValue)
            claims.Add(new Claim(PatientId, account.PatientId.Value.ToString()));

        if (account.DoctorId.HasValue)
            claims.Add(new Claim(DoctorId, account.DoctorId.Value.ToString()));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
