using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface IAppointmentSlotRepository
{
    Task<List<DoctorShift>> GetActiveDoctorShiftsAsync(int? doctorId = null);
    Task<List<DoctorShift>> GetDoctorShiftsByShiftIdAsync(int shiftId);
    Task<List<NonWorkingDay>> GetNonWorkingDaysAsync(DateOnly fromDate, DateOnly toDate);
    Task<List<AppointmentSlot>> GetSlotsTrackingAsync(DateOnly fromDate, DateOnly toDate, int? doctorId = null);
    Task<List<AppointmentSlot>> GetAvailableSlotsAsync(int doctorId, DateOnly fromDate, DateOnly toDate);
    Task AddRangeAsync(IEnumerable<AppointmentSlot> appointmentSlots);
    Task SaveChangesAsync();
}
