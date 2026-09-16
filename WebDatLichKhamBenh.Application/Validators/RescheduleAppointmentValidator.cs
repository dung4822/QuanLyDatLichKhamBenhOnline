using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Appointments;

namespace WebDatLichKhamBenh.Application.Validators;

public class RescheduleAppointmentValidator : AbstractValidator<RescheduleAppointmentRequest>
{
    public RescheduleAppointmentValidator() => RuleFor(x => x.AppointmentSlotId).GreaterThan(0);
}
