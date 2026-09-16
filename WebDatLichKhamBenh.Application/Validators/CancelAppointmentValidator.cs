using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Appointments;

namespace WebDatLichKhamBenh.Application.Validators;

public class CancelAppointmentValidator : AbstractValidator<CancelAppointmentRequest>
{
    public CancelAppointmentValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}
