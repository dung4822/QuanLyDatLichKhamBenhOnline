namespace WebDatLichKhamBenh.Application.DTOs.Appointments;

public class CreateAppointmentRequest
{
    public int PatientId { get; set; }
    public int AppointmentSlotId { get; set; }
    public string? Reason { get; set; }
}
