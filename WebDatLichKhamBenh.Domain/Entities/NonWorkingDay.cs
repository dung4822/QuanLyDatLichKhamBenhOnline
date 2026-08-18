namespace WebDatLichKhamBenh.Domain.Entities;

/// <summary>
/// Ngày bác sĩ nghỉ hoặc ngày cả bệnh viện không làm việc.
/// DoctorId bằng null nghĩa là cả bệnh viện nghỉ.
/// </summary>
public class NonWorkingDay
{
    public int NonWorkingDayId { get; set; }

    public DateOnly Date { get; set; }

    public string? Reason { get; set; }

    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
