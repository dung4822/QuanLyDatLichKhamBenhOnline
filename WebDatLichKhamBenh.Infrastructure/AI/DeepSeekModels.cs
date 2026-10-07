using System.Text.Json;

namespace WebDatLichKhamBenh.Infrastructure.AI;

// Hình dạng JSON gửi/nhận với DeepSeek (cùng định dạng OpenAI Chat Completions).
// Chỉ dùng bên trong Infrastructure; Application chỉ biết LlmMessage/LlmReply.
// Tên property PascalCase được đổi sang snake_case khi serialize (MaxTokens → max_tokens).

internal record DeepSeekRequest(
    string Model,
    List<DeepSeekMessage> Messages,
    int MaxTokens,
    bool Stream,
    DeepSeekThinking Thinking,
    List<DeepSeekTool>? Tools,
    string? ToolChoice);

internal record DeepSeekThinking(string Type);

internal record DeepSeekMessage(
    string Role,
    string? Content,
    List<DeepSeekToolCall>? ToolCalls = null,
    string? ToolCallId = null);

internal record DeepSeekTool(string Type, DeepSeekFunctionDefinition Function);

internal record DeepSeekFunctionDefinition(string Name, string Description, JsonElement Parameters);

internal record DeepSeekToolCall(string Id, string Type, DeepSeekFunctionCall Function);

internal record DeepSeekFunctionCall(string Name, string Arguments);

internal record DeepSeekResponse(List<DeepSeekChoice>? Choices);

internal record DeepSeekChoice(string? FinishReason, DeepSeekMessage? Message);
