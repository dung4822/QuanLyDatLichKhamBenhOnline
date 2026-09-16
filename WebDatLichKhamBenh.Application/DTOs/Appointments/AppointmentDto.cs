using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.DTOs.Appointments;

public class AppointmentDto
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public string PatientFullName { get; set; } = string.Empty;
    public string PatientPhoneNumber { get; set; } = string.Empty;
    public int AppointmentSlotId { get; set; }
    public int DoctorId { get; set; }
    public string DoctorFullName { get; set; } = string.Empty;
    public int SpecialtyId { get; set; }
    public string SpecialtyName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string? Reason { get; set; }
    public AppointmentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}
