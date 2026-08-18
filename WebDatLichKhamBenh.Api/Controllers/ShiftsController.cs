using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.Shifts;
using WebDatLichKhamBenh.Application.Exceptions;
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
    {
        return Ok(await _shiftService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var shift = await _shiftService.GetByIdAsync(id);
        return shift is null
            ? NotFound(new { message = $"Shift with ID {id} does not exist." })
            : Ok(shift);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateShiftRequest request)
    {
        try
        {
            var createdShift = await _shiftService.CreateAsync(request);

            return CreatedAtAction(
                nameof(GetById),
                new { id = createdShift.ShiftId },
                createdShift);
        }
        catch (ValidationException exception)
        {
            return CreateValidationProblem(exception);
        }
        catch (DataPersistenceException)
        {
            return CreateDatabaseProblem();
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateShiftRequest request)
    {
        try
        {
            var updatedShift = await _shiftService.UpdateAsync(id, request);
            return updatedShift is null
                ? NotFound(new { message = $"Shift with ID {id} does not exist." })
                : Ok(updatedShift);
        }
        catch (ValidationException exception)
        {
            return CreateValidationProblem(exception);
        }
        catch (DataPersistenceException)
        {
            return CreateDatabaseProblem();
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var deleted = await _shiftService.DeleteAsync(id);
            return deleted ? NoContent() : NotFound();
        }
        catch (DataPersistenceException)
        {
            return CreateDatabaseProblem();
        }
    }

    private IActionResult CreateValidationProblem(ValidationException exception)
    {
        foreach (var error in exception.Errors)
        {
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        return ValidationProblem(ModelState);
    }

    private IActionResult CreateDatabaseProblem()
    {
        return Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Không thể lưu ca làm việc.",
            detail: "Database không lưu được thay đổi.");
    }
}
