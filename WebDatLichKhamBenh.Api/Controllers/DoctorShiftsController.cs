using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.DoctorShifts;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/doctors/{doctorId:int}/shifts")]
public class DoctorShiftsController : ControllerBase
{
    private readonly IDoctorShiftService _doctorShiftService;

    public DoctorShiftsController(IDoctorShiftService doctorShiftService)
    {
        _doctorShiftService = doctorShiftService;
    }

    /// <summary>
    /// Lấy các ca đang được chọn trong lịch tuần của một bác sĩ.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetWeeklySchedule(int doctorId)
    {
        var doctorShifts = await _doctorShiftService.GetWeeklyScheduleAsync(doctorId);
        return doctorShifts is null
            ? NotFound(new { message = $"Doctor with ID {doctorId} does not exist." })
            : Ok(doctorShifts);
    }

    /// <summary>
    /// Đồng bộ toàn bộ lịch tuần từ các checkbox trên giao diện.
    /// Ca mới được tick sẽ thêm; ca bị bỏ tick sẽ xóa mềm.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> ReplaceWeeklySchedule(
        int doctorId,
        [FromBody] ReplaceDoctorShiftsRequest request)
    {
        try
        {
            var doctorShifts = await _doctorShiftService.ReplaceWeeklyScheduleAsync(doctorId, request);

            return doctorShifts is null
                ? NotFound(new { message = $"Doctor with ID {doctorId} does not exist." })
                : Ok(doctorShifts);
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
            title: "Không thể cập nhật lịch làm việc tuần của bác sĩ.",
            detail: "Database không lưu được thay đổi.");
    }
}
