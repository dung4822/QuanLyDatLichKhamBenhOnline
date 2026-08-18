using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.NonWorkingDays;
using WebDatLichKhamBenh.Application.Exceptions;
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
    {
        return Ok(await _nonWorkingDayService.GetAllAsync(fromDate));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var nonWorkingDay = await _nonWorkingDayService.GetByIdAsync(id);
        return nonWorkingDay is null
            ? NotFound(new { message = $"Non-working day with ID {id} does not exist." })
            : Ok(nonWorkingDay);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNonWorkingDayRequest request)
    {
        try
        {
            var createdNonWorkingDay = await _nonWorkingDayService.CreateAsync(request);

            return CreatedAtAction(
                nameof(GetById),
                new { id = createdNonWorkingDay.NonWorkingDayId },
                createdNonWorkingDay);
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
    public async Task<IActionResult> Update(int id, [FromBody] UpdateNonWorkingDayRequest request)
    {
        try
        {
            var updatedNonWorkingDay = await _nonWorkingDayService.UpdateAsync(id, request);
            return updatedNonWorkingDay is null
                ? NotFound(new { message = $"Non-working day with ID {id} does not exist." })
                : Ok(updatedNonWorkingDay);
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
            var deleted = await _nonWorkingDayService.DeleteAsync(id);
            return deleted ? NoContent() : NotFound();
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
            title: "Không thể lưu lịch nghỉ.",
            detail: "Database không lưu được thay đổi.");
    }
}
