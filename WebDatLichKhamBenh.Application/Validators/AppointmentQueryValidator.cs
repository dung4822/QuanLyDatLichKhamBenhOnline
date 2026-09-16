using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Appointments;

namespace WebDatLichKhamBenh.Application.Validators;

public class AppointmentQueryValidator : AbstractValidator<AppointmentQuery>
{
    public AppointmentQueryValidator()
    {
        RuleFor(x => x.PatientId).GreaterThan(0).When(x => x.PatientId.HasValue);
        RuleFor(x => x.DoctorId).GreaterThan(0).When(x => x.DoctorId.HasValue);
        RuleFor(x => x.SpecialtyId).GreaterThan(0).When(x => x.SpecialtyId.HasValue);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.ToDate).Must((query, to) => !query.FromDate.HasValue ||
                !to.HasValue || to.Value >= query.FromDate.Value)
            .WithMessage("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");
    }
}
