using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.Patients;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController(IAccountService accounts, IAppointmentService appointments) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile() => Ok(await accounts.GetMyProfileAsync());

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdatePatientRequest request)
        => Ok(await accounts.UpdateMyPatientProfileAsync(request));

    [HttpGet("appointments")]
    public async Task<IActionResult> GetAppointments([FromQuery] AppointmentQuery query)
        => Ok(await appointments.GetMineAsync(query));
}
