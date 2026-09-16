using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.DTOs.Appointments;

public class AppointmentQuery
{
    public int? PatientId { get; set; }
    public int? DoctorId { get; set; }
    public int? SpecialtyId { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public AppointmentStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
