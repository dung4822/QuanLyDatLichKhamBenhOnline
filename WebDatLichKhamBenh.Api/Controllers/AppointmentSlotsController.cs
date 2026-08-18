using FluentValidation;
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
    public async Task<IActionResult> GetAvailable(
        int doctorId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate)
    {
        try
        {
            var slots = await _appointmentSlotService.GetAvailableSlotsAsync(doctorId, fromDate, toDate);
            return slots is null
                ? NotFound(new { message = $"Doctor with ID {doctorId} does not exist." })
                : Ok(slots);
        }
        catch (ValidationException exception)
        {
            foreach (var error in exception.Errors)
            {
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }
    }
}
