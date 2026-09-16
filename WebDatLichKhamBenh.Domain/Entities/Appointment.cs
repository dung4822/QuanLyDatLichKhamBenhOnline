using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Domain.Entities;

public class Appointment
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int AppointmentSlotId { get; set; }
    public AppointmentSlot AppointmentSlot { get; set; } = null!;
    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public int SpecialtyId { get; set; }
    public Specialty Specialty { get; set; } = null!;

    // Bản chụp lúc đặt: sửa hồ sơ, chuyên khoa hoặc tái sử dụng slot không đổi lịch sử.
    public string PatientFullName { get; set; } = string.Empty;
    public string PatientPhoneNumber { get; set; } = string.Empty;
    public string DoctorFullName { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string? Reason { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}
