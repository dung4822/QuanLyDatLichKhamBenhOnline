using WebDatLichKhamBenh.Application.DTOs.Specialties;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface ISpecialtyService
{
    Task<List<SpecialtyDto>> GetAllAsync();
#if false
    Task<SpecialtyDto?> GetByIdAsync(int specialtyId);
#endif
    Task<SpecialtyDto?> GetByIdAsync(int specialtyId);
    Task<SpecialtyDto> CreateAsync(CreateSpecialtyRequest createSpecialtyRequest);

    Task<SpecialtyDto?> UpdateAsync(int specialtyId, UpdateSpecialtyRequest updateSpecialtyRequest);

    Task<bool> DeleteAsync(int specialtyId);

}
