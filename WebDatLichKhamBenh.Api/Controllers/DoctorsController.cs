using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Application.DTOs.Images;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using CreateDoctorApiRequest = WebDatLichKhamBenh.Api.Models.Doctors.CreateDoctorRequest;
using UpdateDoctorApiRequest = WebDatLichKhamBenh.Api.Models.Doctors.UpdateDoctorRequest;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/doctors")]
public class DoctorsController : ControllerBase
{
    private const long MaximumMultipartRequestSize = 6 * 1024 * 1024;
    private readonly IDoctorService _doctorService;

    public DoctorsController(IDoctorService doctorService)
    {
        _doctorService = doctorService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll([FromQuery] int? specialtyId)
        => Ok(await _doctorService.GetAllAsync(specialtyId));

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(int id)
    {
        var doctor = await _doctorService.GetByIdAsync(id);
        return doctor is null ? NotFound() : Ok(doctor);
    }

    // Nhận multipart/form-data vì có file ảnh; chuyển IFormFile thành ImageUploadRequest
    // để tầng Application không phụ thuộc ASP.NET Core.
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumMultipartRequestSize)]
    public async Task<IActionResult> Create([FromForm] CreateDoctorApiRequest request, CancellationToken cancellationToken)
    {
        using var avatarStream = request.Avatar?.OpenReadStream();
        var createRequest = new CreateDoctorRequest
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

        var doctor = await _doctorService.CreateAsync(createRequest, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = doctor.DoctorId }, doctor);
    }

    [HttpPut("{id:int}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumMultipartRequestSize)]
    public async Task<IActionResult> Update(int id, [FromForm] UpdateDoctorApiRequest request, CancellationToken cancellationToken)
    {
        using var avatarStream = request.Avatar?.OpenReadStream();
        var updateRequest = new UpdateDoctorRequest
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

        var doctor = await _doctorService.UpdateAsync(id, updateRequest, cancellationToken);
        return doctor is null ? NotFound() : Ok(doctor);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _doctorService.DeleteAsync(id) ? NoContent() : NotFound();

    private static ImageUploadRequest? ToImageUpload(IFormFile? file, Stream? content)
    {
        if (file is null || content is null)
            return null;

        return new ImageUploadRequest(content, Path.GetFileName(file.FileName), file.ContentType, file.Length);
    }
}
