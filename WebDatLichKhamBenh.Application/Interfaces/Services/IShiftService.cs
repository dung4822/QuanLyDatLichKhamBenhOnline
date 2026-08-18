using WebDatLichKhamBenh.Application.DTOs.Shifts;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IShiftService
{
    Task<List<ShiftDto>> GetAllAsync();
    Task<ShiftDto?> GetByIdAsync(int shiftId);
    Task<ShiftDto> CreateAsync(CreateShiftRequest createShiftRequest);
    Task<ShiftDto?> UpdateAsync(int shiftId, UpdateShiftRequest updateShiftRequest);
    Task<bool> DeleteAsync(int shiftId);
}
