using WebDatLichKhamBenh.Application.DTOs.AppointmentSlots;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IAppointmentSlotService
{
    Task<List<AppointmentSlotDto>?> GetAvailableSlotsAsync(int doctorId, DateOnly? fromDate, DateOnly? toDate);
    Task EnsureRollingWindowAsync(CancellationToken cancellationToken = default);
    Task SyncRangeAsync(DateOnly fromDate, DateOnly toDate, int? doctorId = null, CancellationToken cancellationToken = default);
}
