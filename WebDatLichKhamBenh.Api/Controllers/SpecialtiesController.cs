using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.Specialties;
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

  /*  [HttpGet("{id}")]*/
#if false
    public async Task<IActionResult> GetById(int id)
    {
        var specialty = await _specialtyService.GetByIdAsync(id);
        return specialty == null ? NotFound() : Ok(specialty);
    }
#endif
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var specialty = await _specialtyService.GetByIdAsync(id);
        return specialty == null ? NotFound() : Ok(specialty);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSpecialtyRequest createSpecialtyRequest)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var createdSpecialty = await _specialtyService.CreateAsync(createSpecialtyRequest);
        return CreatedAtAction(nameof(GetById), new { id = createdSpecialty.SpecialtyId }, createdSpecialty);
    }

    [HttpPut("{id}")]

    public async Task<IActionResult> Update(int id, [FromBody] UpdateSpecialtyRequest updateSpecialtyRequest)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var updatedSpecialty = await _specialtyService.UpdateAsync(id, updateSpecialtyRequest);
        return updatedSpecialty == null ? NotFound() : Ok(updatedSpecialty);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _specialtyService.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
