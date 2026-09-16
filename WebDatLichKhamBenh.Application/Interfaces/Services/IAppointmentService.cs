using WebDatLichKhamBenh.Application.DTOs.Appointments;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IAppointmentService
{
    Task<AppointmentPageDto> GetAllAsync(AppointmentQuery query);
    Task<AppointmentPageDto> GetMineAsync(AppointmentQuery query);
    Task<List<AppointmentHistoryDto>> GetHistoryAsync(int id, int page = 1, int pageSize = 50);
    Task<AppointmentDto?> GetByIdAsync(int id);
    Task<AppointmentDto> CreateAsync(CreateAppointmentRequest request, CancellationToken cancellationToken = default);
    Task<AppointmentDto> RescheduleAsync(int id, RescheduleAppointmentRequest request, CancellationToken cancellationToken = default);
    Task<AppointmentDto> CancelAsync(int id, CancelAppointmentRequest request, CancellationToken cancellationToken = default);
    Task<AppointmentDto> CompleteAsync(int id, CancellationToken cancellationToken = default);
    Task<AppointmentDto> MarkNoShowAsync(int id, CancellationToken cancellationToken = default);
}
