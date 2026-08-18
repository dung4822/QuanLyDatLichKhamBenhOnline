using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Shifts;

namespace WebDatLichKhamBenh.Application.Validators;

public class UpdateShiftValidator : AbstractValidator<UpdateShiftRequest>
{
    public UpdateShiftValidator()
    {
        RuleFor(shift => shift.DayOfWeek).IsInEnum();

        RuleFor(shift => shift.Name)
            .Cascade(CascadeMode.Stop)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Tên ca làm việc là bắt buộc.")
            .MaximumLength(100);

        RuleFor(shift => shift.EndTime)
            .GreaterThan(shift => shift.StartTime)
            .WithMessage("Giờ kết thúc phải sau giờ bắt đầu.")
            .Must((shift, endTime) => IsValidSlotDuration(shift.StartTime, endTime))
            .WithMessage("Ca làm việc phải dài ít nhất 30 phút và chia hết cho 30 phút.");
    }

    private static bool IsValidSlotDuration(TimeOnly startTime, TimeOnly endTime)
    {
        var durationMinutes = (endTime - startTime).TotalMinutes;
        return durationMinutes >= 30 && durationMinutes % 30 == 0;
    }
}
