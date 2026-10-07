using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.NonWorkingDays;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/non-working-days")]
public class NonWorkingDaysController : ControllerBase
{
    private readonly INonWorkingDayService _nonWorkingDayService;

    public NonWorkingDaysController(INonWorkingDayService nonWorkingDayService)
    {
        _nonWorkingDayService = nonWorkingDayService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateOnly? fromDate)
        => Ok(await _nonWorkingDayService.GetAllAsync(fromDate));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var nonWorkingDay = await _nonWorkingDayService.GetByIdAsync(id);
        return nonWorkingDay is null ? NotFound() : Ok(nonWorkingDay);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNonWorkingDayRequest request)
    {
        var nonWorkingDay = await _nonWorkingDayService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = nonWorkingDay.NonWorkingDayId }, nonWorkingDay);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateNonWorkingDayRequest request)
    {
        var nonWorkingDay = await _nonWorkingDayService.UpdateAsync(id, request);
        return nonWorkingDay is null ? NotFound() : Ok(nonWorkingDay);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _nonWorkingDayService.DeleteAsync(id) ? NoContent() : NotFound();
}
