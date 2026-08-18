namespace WebDatLichKhamBenh.Application.DTOs.DoctorShifts;

/// <summary>
/// Toàn bộ các ca được chọn trên giao diện cho một bác sĩ.
/// Danh sách rỗng có nghĩa là bác sĩ không làm ca nào trong lịch tuần.
/// </summary>
public record ReplaceDoctorShiftsRequest
{
    public List<int> ShiftIds { get; init; } = [];
}
