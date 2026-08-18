using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.DTOs.AppointmentSlots;

public class AppointmentSlotDto
{
    public int AppointmentSlotId { get; set; }
    public int DoctorId { get; set; }
    public int DoctorShiftId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public AppointmentSlotStatus Status { get; set; }
    public string? UnavailableReason { get; set; }
}
