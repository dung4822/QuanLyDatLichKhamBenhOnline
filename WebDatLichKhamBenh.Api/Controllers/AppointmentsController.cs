using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _service;
    public AppointmentsController(IAppointmentService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] AppointmentQuery query)
        => Ok(await _service.GetAllAsync(query));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var appointment = await _service.GetByIdAsync(id);
        return appointment is null ? NotFound(new { message = "Lịch hẹn không tồn tại." }) : Ok(appointment);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentRequest request, CancellationToken cancellationToken)
    {
        var appointment = await _service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = appointment.AppointmentId }, appointment);
    }

    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> GetHistory(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _service.GetHistoryAsync(id, page, pageSize));

    [HttpPatch("{id:int}/reschedule")]
    public async Task<IActionResult> Reschedule(int id, [FromBody] RescheduleAppointmentRequest request, CancellationToken cancellationToken)
        => Ok(await _service.RescheduleAsync(id, request, cancellationToken));

    [HttpPatch("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelAppointmentRequest request, CancellationToken cancellationToken)
        => Ok(await _service.CancelAsync(id, request, cancellationToken));

    [HttpPatch("{id:int}/complete")]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken)
        => Ok(await _service.CompleteAsync(id, cancellationToken));

    [HttpPatch("{id:int}/no-show")]
    public async Task<IActionResult> MarkNoShow(int id, CancellationToken cancellationToken)
        => Ok(await _service.MarkNoShowAsync(id, cancellationToken));
}
