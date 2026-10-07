using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Chats;

namespace WebDatLichKhamBenh.Application.Validators;

public class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public const int MaxMessageLength = 4000;
    public const int MaxHistoryMessages = 10;
    public const int MaxTotalLength = 16000;

    public ChatRequestValidator()
    {
        // Gặp lỗi đầu tiên là dừng, nên các rule phía sau không phải kiểm tra null nữa.
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Message).NotEmpty().MaximumLength(MaxMessageLength);

        RuleFor(x => x.History)
            .NotNull()
            .Must(history => history.Count <= MaxHistoryMessages && history.Count % 2 == 0)
            .WithMessage("History gồm tối đa 5 cặp tin nhắn user/assistant.")
            .Must(IsUserAssistantPairs)
            .WithMessage("History phải luân phiên user rồi assistant.");

        RuleForEach(x => x.History).ChildRules(message =>
            message.RuleFor(m => m.Content).NotEmpty().MaximumLength(MaxMessageLength));

        RuleFor(x => x)
            .Must(x => x.Message.Length + x.History.Sum(m => m.Content.Length) <= MaxTotalLength)
            .WithName("History")
            .WithMessage($"Tổng nội dung Message và History không được quá {MaxTotalLength} ký tự.");
    }

    // Client không được tự chèn vai trò "system" hay "tool" để điều khiển mô hình.
    private static bool IsUserAssistantPairs(List<ChatHistoryMessage> history)
    {
        for (var i = 0; i < history.Count; i++)
        {
            var expectedRole = i % 2 == 0 ? "user" : "assistant";
            if (history[i]?.Role != expectedRole) return false;
        }
        return true;
    }
}
