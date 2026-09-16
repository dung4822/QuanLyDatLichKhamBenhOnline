using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Appointments;

namespace WebDatLichKhamBenh.Application.Validators;

public class CreateAppointmentValidator : AbstractValidator<CreateAppointmentRequest>
{
    public CreateAppointmentValidator()
    {
        RuleFor(x => x.PatientId).GreaterThan(0);
        RuleFor(x => x.AppointmentSlotId).GreaterThan(0);
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}
