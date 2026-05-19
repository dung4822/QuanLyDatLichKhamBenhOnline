using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpecialtiesController : ControllerBase
{
    private readonly ISpecialtyService _specialtyService;

    public SpecialtiesController(ISpecialtyService specialtyService)
    {
        _specialtyService = specialtyService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var specialties = await _specialtyService.GetAllAsync();
        return Ok(specialties);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var specialty = await _specialtyService.GetByIdAsync(id);
        return specialty == null ? NotFound() : Ok(specialty);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSpecialtyDto createSpecialtyDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var createdSpecialty = await _specialtyService.CreateAsync(createSpecialtyDto);
        return CreatedAtAction(nameof(GetById), new { id = createdSpecialty.SpecialtyId }, createdSpecialty);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSpecialtyDto updateSpecialtyDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var updatedSpecialty = await _specialtyService.UpdateAsync(id, updateSpecialtyDto);
        return updatedSpecialty == null ? NotFound() : Ok(updatedSpecialty);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _specialtyService.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
