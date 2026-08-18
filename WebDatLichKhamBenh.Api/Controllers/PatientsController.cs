using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.Patients;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/patients")]
public class PatientsController : ControllerBase
{
    private readonly IPatientService _patientService;

    public PatientsController(IPatientService patientService)
    {
        _patientService = patientService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _patientService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var patient = await _patientService.GetByIdAsync(id);

        return patient is null
            ? NotFound(new { message = $"Patient with ID {id} does not exist." })
            : Ok(patient);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePatientRequest request)
    {


        try
        {
            var createdPatient = await _patientService.CreateAsync(request);

            return CreatedAtAction(
                nameof(GetById),
                new { id = createdPatient.PatientId },
                createdPatient);
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
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdatePatientRequest request)
    {
        try
        {
            var updatedPatient = await _patientService.UpdateAsync(id, request);

            return updatedPatient is null
                ? NotFound(new { message = $"Patient with ID {id} does not exist." })
                : Ok(updatedPatient);
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
            var deleted = await _patientService.DeleteAsync(id);
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
            title: "Không thể lưu Patient.",
            detail: "Database không lưu được thay đổi.");
    }
}
