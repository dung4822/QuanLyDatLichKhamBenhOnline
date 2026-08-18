using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.NonWorkingDays;
using WebDatLichKhamBenh.Application.Time;

namespace WebDatLichKhamBenh.Application.Validators;

public class CreateNonWorkingDayValidator : AbstractValidator<CreateNonWorkingDayRequest>
{
    public CreateNonWorkingDayValidator()
    {
        RuleFor(nonWorkingDay => nonWorkingDay.Date)
            .Must(date => date > ClinicClock.Today)
            .WithMessage("Ngày nghỉ phải là một ngày trong tương lai.");

        RuleFor(nonWorkingDay => nonWorkingDay.DoctorId)
            .GreaterThan(0)
            .When(nonWorkingDay => nonWorkingDay.DoctorId.HasValue)
            .WithMessage("DoctorId phải lớn hơn 0 hoặc để null nếu cả bệnh viện nghỉ.");

        RuleFor(nonWorkingDay => nonWorkingDay.Reason)
            .MaximumLength(500);
    }
}
