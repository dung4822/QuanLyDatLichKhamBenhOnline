namespace WebDatLichKhamBenh.Domain.Entities;

/// <summary>
/// Mẫu lịch làm việc lặp lại hằng tuần của một bác sĩ.
/// Bản ghi này không phải là một lần làm việc thực tế theo ngày cụ thể.
/// </summary>
public class DoctorShift
{
    public int DoctorShiftId { get; set; }

    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public int ShiftId { get; set; }
    public Shift Shift { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Giữ lịch sử phân công để không làm mất dấu các slot/hẹn đã được tạo trước đó.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<AppointmentSlot> AppointmentSlots { get; set; } = new List<AppointmentSlot>();
}
