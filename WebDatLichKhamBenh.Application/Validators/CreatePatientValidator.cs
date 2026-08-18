using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Patients;

namespace WebDatLichKhamBenh.Application.Validators;

public class CreatePatientValidator : AbstractValidator<CreatePatientRequest>
{
    public CreatePatientValidator()
    {
        RuleFor(patient => patient.FullName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(fullName => fullName.Trim().Length is >= 3 and <= 100)
            .WithMessage("FullName phải có từ 3 đến 100 ký tự.");

        RuleFor(patient => patient.DateOfBirth)
            .NotEmpty()
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.Today));

        RuleFor(patient => patient.Gender).InclusiveBetween(0, 1);

        RuleFor(patient => patient.PhoneNumber)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(patient => patient.Email)
            .EmailAddress()
            .MaximumLength(50)
            .When(patient => !string.IsNullOrWhiteSpace(patient.Email));

        RuleFor(patient => patient.Address).MaximumLength(255);
    }
}
