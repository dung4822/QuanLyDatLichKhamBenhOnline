using WebDatLichKhamBenh.Application.DTOs;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface ISpecialtyService
{
    Task<List<SpecialtyDto>> GetAllAsync();
    Task<SpecialtyDto?> GetByIdAsync(int specialtyId);
    Task<SpecialtyDto> CreateAsync(CreateSpecialtyDto createSpecialtyDto);
    Task<SpecialtyDto?> UpdateAsync(int specialtyId, UpdateSpecialtyDto updateSpecialtyDto);
    Task<bool> DeleteAsync(int specialtyId);
}
