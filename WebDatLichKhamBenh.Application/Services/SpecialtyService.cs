using WebDatLichKhamBenh.Application.DTOs.Specialties;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Services;

public class SpecialtyService : ISpecialtyService
{
    private readonly ISpecialtyRepository _specialtyRepository;

    public SpecialtyService(ISpecialtyRepository specialtyRepository)
    {
        _specialtyRepository = specialtyRepository;
    }

    public async Task<List<SpecialtyDto>> GetAllAsync()
    {
        var specialties = await _specialtyRepository.GetAllAsync();
        return specialties.Select(MapToDto).ToList();
    }

#if false
    public async Task<SpecialtyDto?> GetByIdAsync(int specialtyId)
    {
        var specialty = await _specialtyRepository.GetByIdAsync(specialtyId);
        return specialty == null ? null : MapToDto(specialty);
    }
#endif

    public async Task<SpecialtyDto> CreateAsync(CreateSpecialtyDto createSpecialtyDto)
    {
        var specialty = new Specialty
        {
            Name = createSpecialtyDto.Name.Trim(),
            Description = createSpecialtyDto.Description?.Trim(),
            Status = createSpecialtyDto.Status.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _specialtyRepository.AddAsync(specialty);
        await _specialtyRepository.SaveChangesAsync();

        return MapToDto(specialty);
    }

    public async Task<SpecialtyDto?> UpdateAsync(int specialtyId, UpdateSpecialtyDto updateSpecialtyDto)
    {
        var specialty = await _specialtyRepository.GetByIdTrackingAsync(specialtyId);
        if (specialty == null)
        {
            return null;
        }
        if(updateSpecialtyDto.Name == specialty.Name &&
            updateSpecialtyDto.Description == specialty.Description &&
            updateSpecialtyDto.Status == specialty.Status) return MapToDto(specialty);


        specialty.Name = updateSpecialtyDto.Name.Trim();
        specialty.Description = updateSpecialtyDto.Description?.Trim();
        specialty.Status = updateSpecialtyDto.Status.Trim();
        specialty.UpdatedAt = DateTime.UtcNow;

        await _specialtyRepository.SaveChangesAsync();
        return MapToDto(specialty);
    }


    public async Task<bool> DeleteAsync(int specialtyId)
    {
        var specialty = await _specialtyRepository.GetByIdAsync(specialtyId);
        if (specialty == null)
        {
            return false;
        }

        specialty.Status = "Inactive";
        specialty.UpdatedAt = DateTime.UtcNow;

        _specialtyRepository.Update(specialty);
        await _specialtyRepository.SaveChangesAsync();

        return true;
    }

    private static SpecialtyDto MapToDto(Specialty specialty)
    {
        return new SpecialtyDto
        {
            SpecialtyId = specialty.SpecialtyId,
            Name = specialty.Name,
            Description = specialty.Description,
            Status = specialty.Status,
            CreatedAt = specialty.CreatedAt,
            UpdatedAt = specialty.UpdatedAt
        };
    }

    public async Task<SpecialtyDto?> GetByIdAsync(int specialtyId)
    {
        var specialty = await _specialtyRepository.GetByIdAsync(specialtyId);
        return specialty == null ? null : MapToDto(specialty);
    }
}
