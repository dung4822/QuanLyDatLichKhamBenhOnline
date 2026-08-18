using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Domain.Entities;

/// <summary>
/// Một khoảng khám cụ thể của bác sĩ trong một ngày.
/// Slot được giữ lại khi lịch tuần thay đổi để bảo toàn lịch sử đặt hẹn.
/// </summary>
public class AppointmentSlot
{
    public int AppointmentSlotId { get; set; }

    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public int DoctorShiftId { get; set; }
    public DoctorShift DoctorShift { get; set; } = null!;

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public AppointmentSlotStatus Status { get; set; } = AppointmentSlotStatus.Available;
    public string? UnavailableReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
