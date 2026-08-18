using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Doctors;

namespace WebDatLichKhamBenh.Application.Validators;

public class CreateDoctorValidator : AbstractValidator<CreateDoctorRequest>
{
    public CreateDoctorValidator()
    {
        RuleFor(doctor => doctor.FullName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(fullName =>
            {
                var length = fullName.Trim().Length;
                return length is >= 3 and <= 100;
            })
            .WithMessage("FullName phải có từ 3 đến 100 ký tự.");

        RuleFor(doctor => doctor.Email)
            .EmailAddress()
            .MaximumLength(50)
            .When(doctor => !string.IsNullOrWhiteSpace(doctor.Email));

        RuleFor(doctor => doctor.PhoneNumber).MaximumLength(20);
        RuleFor(doctor => doctor.Address).MaximumLength(255);
        RuleFor(doctor => doctor.SpecialtyId).GreaterThan(0);
        RuleFor(doctor => doctor.Gender).InclusiveBetween(0, 1);

        RuleFor(doctor => doctor.CareerStartDate)
            .NotEmpty()
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.Today));

        When(
            doctor => doctor.Avatar is not null,
            () => RuleFor(doctor => doctor.Avatar!)
                .SetValidator(new ImageUploadValidator()));
    }
}
