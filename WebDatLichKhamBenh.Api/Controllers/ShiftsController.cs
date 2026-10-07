using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.Shifts;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/shifts")]
public class ShiftsController : ControllerBase
{
    private readonly IShiftService _shiftService;

    public ShiftsController(IShiftService shiftService)
    {
        _shiftService = shiftService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _shiftService.GetAllAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var shift = await _shiftService.GetByIdAsync(id);
        return shift is null ? NotFound() : Ok(shift);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateShiftRequest request)
    {
        var shift = await _shiftService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = shift.ShiftId }, shift);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateShiftRequest request)
    {
        var shift = await _shiftService.UpdateAsync(id, request);
        return shift is null ? NotFound() : Ok(shift);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _shiftService.DeleteAsync(id) ? NoContent() : NotFound();
}
