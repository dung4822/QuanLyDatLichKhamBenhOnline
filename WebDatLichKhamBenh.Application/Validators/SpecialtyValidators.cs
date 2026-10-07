using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Specialties;

namespace WebDatLichKhamBenh.Application.Validators;

// Giữ nguyên các giới hạn đang có trước đây ở DataAnnotations ([Required], [MaxLength]).
public class CreateSpecialtyValidator : AbstractValidator<CreateSpecialtyRequest>
{
    public CreateSpecialtyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Status).NotEmpty().MaximumLength(20);
    }
}

public class UpdateSpecialtyValidator : AbstractValidator<UpdateSpecialtyRequest>
{
    public UpdateSpecialtyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(20);
    }
}
