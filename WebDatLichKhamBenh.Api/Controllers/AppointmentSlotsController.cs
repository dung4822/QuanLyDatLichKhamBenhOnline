using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/doctors/{doctorId:int}/appointment-slots")]
public class AppointmentSlotsController : ControllerBase
{
    private readonly IAppointmentSlotService _appointmentSlotService;

    public AppointmentSlotsController(IAppointmentSlotService appointmentSlotService)
    {
        _appointmentSlotService = appointmentSlotService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAvailable(int doctorId, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate)
    {
        var slots = await _appointmentSlotService.GetAvailableSlotsAsync(doctorId, fromDate, toDate);
        return slots is null ? NotFound() : Ok(slots);
    }
}
