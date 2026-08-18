namespace WebDatLichKhamBenh.Domain.Entities;

/// <summary>
/// Ca làm việc dùng chung trong hệ thống, được lặp lại theo từng ngày trong tuần.
/// </summary>
public class Shift
{
    public int ShiftId { get; set; }

    // Dùng enum System.DayOfWeek: Sunday, Monday, ..., Saturday.
    public DayOfWeek DayOfWeek { get; set; }

    public string Name { get; set; } = string.Empty;

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    // Các bác sĩ được phân công vào ca này.
    public ICollection<DoctorShift> DoctorShifts { get; set; } = new List<DoctorShift>();
}
