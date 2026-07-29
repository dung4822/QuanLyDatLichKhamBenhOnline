using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Api.Models.Doctors;
using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Application.DTOs.Images;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[Route("api/doctors")]
[ApiController]
public class DoctorsController : ControllerBase
{
    private const long MaximumMultipartRequestSize = 6 * 1024 * 1024;
    private readonly IDoctorService _doctorService;

    public DoctorsController(IDoctorService doctorService)
    {
        _doctorService = doctorService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _doctorService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var doctor = await _doctorService.GetByIdAsync(id);

        return doctor is null
            ? NotFound(new { message = $"Doctor with ID {id} does not exist." })
            : Ok(doctor);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumMultipartRequestSize)]
    public async Task<IActionResult> Create([FromForm] CreateDoctorRequest request)
    {
        using var avatarStream = request.Avatar?.OpenReadStream();

        var createDoctorDto = new CreateDoctorDto
        {
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            Email = request.Email,
            Gender = request.Gender,
            Address = request.Address,
            CareerStartDate = request.CareerStartDate,
            SpecialtyId = request.SpecialtyId,
            Avatar = ToImageUpload(request.Avatar, avatarStream)
        };

        try
        {
            var createdDoctor = await _doctorService.CreateAsync(
                createDoctorDto,
                HttpContext.RequestAborted);

            return CreatedAtAction(
                nameof(GetById),
                new { id = createdDoctor.DoctorId },
                createdDoctor);
        }
        catch (ValidationException exception)
        {
            return CreateValidationProblem(exception);
        }
        catch (ImageStorageException)
        {
            return CreateImageStorageProblem();
        }
        catch (DataPersistenceException)
        {
            return CreateDatabaseProblem();
        }
    }

    [HttpPut("{id:int}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumMultipartRequestSize)]
    public async Task<IActionResult> Update(
        int id,
        [FromForm] UpdateDoctorRequest request)
    {
        using var avatarStream = request.Avatar?.OpenReadStream();

        var updateDoctorDto = new UpdateDoctorDto
        {
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            Email = request.Email,
            Gender = request.Gender,
            Address = request.Address,
            CareerStartDate = request.CareerStartDate,
            SpecialtyId = request.SpecialtyId,
            RemoveAvatar = request.RemoveAvatar,
            Avatar = ToImageUpload(request.Avatar, avatarStream)
        };

        try
        {
            var updatedDoctor = await _doctorService.UpdateAsync(
                id,
                updateDoctorDto,
                HttpContext.RequestAborted);

            return updatedDoctor is null
                ? NotFound(new { message = $"Doctor with ID {id} does not exist." })
                : Ok(updatedDoctor);
        }
        catch (ValidationException exception)
        {
            return CreateValidationProblem(exception);
        }
        catch (ImageStorageException)
        {
            return CreateImageStorageProblem();
        }
        catch (DataPersistenceException)
        {
            return CreateDatabaseProblem();
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _doctorService.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    private static ImageUploadDto? ToImageUpload(IFormFile? file, Stream? content)
    {
        if (file is null || content is null)
        {
            return null;
        }

        return new ImageUploadDto(
            content,
            Path.GetFileName(file.FileName),
            file.ContentType,
            file.Length);
    }

    private IActionResult CreateValidationProblem(ValidationException exception)
    {
        foreach (var error in exception.Errors)
        {
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        return ValidationProblem(ModelState);
    }

    private IActionResult CreateImageStorageProblem()
    {
        return Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Không thể lưu ảnh.",
            detail: "Dịch vụ lưu trữ ảnh đang không khả dụng. Dữ liệu Doctor chưa bị thay đổi.");
    }

    private IActionResult CreateDatabaseProblem()
    {
        return Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Không thể lưu Doctor.",
            detail: "Database không lưu được thay đổi. Ảnh mới đã được rollback nếu có thể.");
    }
}
