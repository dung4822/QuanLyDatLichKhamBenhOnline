namespace WebDatLichKhamBenh.Application.DTOs.Appointments;

public class AppointmentPageDto
{
    public List<AppointmentDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
