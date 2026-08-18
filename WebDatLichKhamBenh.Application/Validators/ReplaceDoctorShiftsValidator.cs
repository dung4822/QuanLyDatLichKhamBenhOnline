using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.DoctorShifts;

namespace WebDatLichKhamBenh.Application.Validators;

public class ReplaceDoctorShiftsValidator : AbstractValidator<ReplaceDoctorShiftsRequest>
{
    public ReplaceDoctorShiftsValidator()
    {
        RuleFor(request => request.ShiftIds)
            .NotNull()
            .WithMessage("Danh sách ShiftIds là bắt buộc.");

        RuleForEach(request => request.ShiftIds)
            .GreaterThan(0)
            .WithMessage("Mỗi ShiftId phải lớn hơn 0.");

        RuleFor(request => request.ShiftIds)
            .Must(shiftIds => shiftIds is null || shiftIds.Distinct().Count() == shiftIds.Count)
            .WithMessage("Danh sách ShiftIds không được chứa ca trùng lặp.");
    }
}
