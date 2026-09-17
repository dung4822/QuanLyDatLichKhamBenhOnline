using System.Security.Claims;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Api.Security;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
        ? user : throw new AuthenticationRequiredException();
    public Guid AccountId => Guid.Parse(Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public Guid SessionId => Guid.Parse(Principal.FindFirstValue(ClinicClaims.SessionId)!);
    public string DisplayName => Principal.FindFirstValue(ClaimTypes.Name)!;
    public AccountRole Role => Enum.Parse<AccountRole>(Principal.FindFirstValue(ClaimTypes.Role)!);
    public int? PatientId => int.TryParse(Principal.FindFirstValue(ClinicClaims.PatientId), out var id) ? id : null;
    public int? DoctorId => int.TryParse(Principal.FindFirstValue(ClinicClaims.DoctorId), out var id) ? id : null;
}
