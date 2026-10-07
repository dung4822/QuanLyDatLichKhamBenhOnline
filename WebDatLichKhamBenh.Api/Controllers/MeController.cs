using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.Patients;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IAccountService _accounts;
    private readonly IAppointmentService _appointments;

    public MeController(
        IAccountService accounts,
        IAppointmentService appointments)
    {
        _accounts = accounts;
        _appointments = appointments;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile() => Ok(await _accounts.GetMyProfileAsync());

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdatePatientRequest request)
        => Ok(await _accounts.UpdateMyPatientProfileAsync(request));

    [HttpGet("appointments")]
    public async Task<IActionResult> GetAppointments([FromQuery] AppointmentQuery query)
        => Ok(await _appointments.GetMineAsync(query));
}
