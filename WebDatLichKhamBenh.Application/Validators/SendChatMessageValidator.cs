using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Chats;

namespace WebDatLichKhamBenh.Application.Validators;

public class SendChatMessageValidator : AbstractValidator<SendChatMessageRequest>
{
    public const int MaxMessageLength = 4000;

    public SendChatMessageValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(MaxMessageLength);
        RuleFor(x => x.ClientMessageId).NotEqual(Guid.Empty).When(x => x.ClientMessageId.HasValue);
    }
}
