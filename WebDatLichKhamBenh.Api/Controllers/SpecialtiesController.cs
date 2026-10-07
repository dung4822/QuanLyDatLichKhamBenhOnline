using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.Specialties;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/specialties")]
public class SpecialtiesController : ControllerBase
{
    private readonly ISpecialtyService _specialtyService;

    public SpecialtiesController(ISpecialtyService specialtyService)
    {
        _specialtyService = specialtyService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
        => Ok(await _specialtyService.GetAllAsync());

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(int id)
    {
        var specialty = await _specialtyService.GetByIdAsync(id);
        return specialty is null ? NotFound() : Ok(specialty);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSpecialtyRequest request)
    {
        var specialty = await _specialtyService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = specialty.SpecialtyId }, specialty);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSpecialtyRequest request)
    {
        var specialty = await _specialtyService.UpdateAsync(id, request);
        return specialty is null ? NotFound() : Ok(specialty);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _specialtyService.DeleteAsync(id) ? NoContent() : NotFound();
}
