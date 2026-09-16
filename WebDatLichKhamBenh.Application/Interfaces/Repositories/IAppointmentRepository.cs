using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(int id, bool tracking = false);
    Task<(List<Appointment> Items, int TotalCount)> GetAllAsync(AppointmentQuery query);
    Task<AppointmentSlot?> GetSlotAsync(int slotId);
    Task<bool> HasDoctorOverlapAsync(int doctorId, DateOnly date, TimeOnly startTime, TimeOnly endTime, int? excludeAppointmentId = null);
    Task<bool> HasPatientOverlapAsync(int patientId, DateOnly date, TimeOnly startTime, TimeOnly endTime, int? excludeAppointmentId = null);
    Task<bool> HasUpcomingConfirmedAsync(DateTime clinicNow, int? doctorId = null,
        int? patientId = null, int? specialtyId = null, int? shiftId = null,
        int[]? doctorShiftIds = null, DateOnly? date = null);
    Task<bool> TryBookSlotAsync(int slotId);
    Task ReleaseSlotAsync(int slotId, string? unavailableReason);
    Task AddAsync(Appointment appointment);
    Task AddHistoryAsync(AppointmentHistory history);
    Task<List<AppointmentHistory>> GetHistoryAsync(int appointmentId, int page, int pageSize);
    Task SaveChangesAsync();
}
