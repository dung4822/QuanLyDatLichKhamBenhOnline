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

    public async Task<SpecialtyDto> CreateAsync(CreateSpecialtyRequest createSpecialtyRequest)
    {
        var specialty = new Specialty
        {
            Name = createSpecialtyRequest.Name.Trim(),
            Description = createSpecialtyRequest.Description?.Trim(),
            Status = createSpecialtyRequest.Status.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _specialtyRepository.AddAsync(specialty);
        await _specialtyRepository.SaveChangesAsync();

        return MapToDto(specialty);
    }

    public async Task<SpecialtyDto?> UpdateAsync(int specialtyId, UpdateSpecialtyRequest updateSpecialtyRequest)
    {
        var specialty = await _specialtyRepository.GetByIdTrackingAsync(specialtyId);
        if (specialty == null)
        {
            return null;
        }
        if(updateSpecialtyRequest.Name == specialty.Name &&
            updateSpecialtyRequest.Description == specialty.Description &&
            updateSpecialtyRequest.Status == specialty.Status) return MapToDto(specialty);


        specialty.Name = updateSpecialtyRequest.Name.Trim();
        specialty.Description = updateSpecialtyRequest.Description?.Trim();
        specialty.Status = updateSpecialtyRequest.Status.Trim();
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
