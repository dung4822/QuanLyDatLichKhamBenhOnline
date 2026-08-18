using WebDatLichKhamBenh.Application.DTOs.NonWorkingDays;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface INonWorkingDayService
{
    Task<List<NonWorkingDayDto>> GetAllAsync(DateOnly? fromDate);
    Task<NonWorkingDayDto?> GetByIdAsync(int nonWorkingDayId);
    Task<NonWorkingDayDto> CreateAsync(CreateNonWorkingDayRequest createNonWorkingDayRequest);
    Task<NonWorkingDayDto?> UpdateAsync(int nonWorkingDayId, UpdateNonWorkingDayRequest updateNonWorkingDayRequest);
    Task<bool> DeleteAsync(int nonWorkingDayId);

    // AppointmentSlotService sẽ dùng hàm này để chặn slot nếu bác sĩ hoặc cả bệnh viện nghỉ.
    Task<bool> IsDoctorUnavailableAsync(int doctorId, DateOnly date);
}
