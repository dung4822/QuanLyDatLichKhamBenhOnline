using System.Text.Json;

namespace WebDatLichKhamBenh.Application.DTOs.Chats;

// Hợp đồng nội bộ; client chỉ được gửi lịch sử user/assistant qua ChatRequest.
public record LlmMessage(string Role, string? Content,
    IReadOnlyList<LlmToolCall>? ToolCalls = null, string? ToolCallId = null);

public record LlmToolCall(string Id, string Name, string Arguments);

public record LlmToolDefinition(string Name, string Description, JsonElement Parameters);

public record LlmReply(string? Content, IReadOnlyList<LlmToolCall> ToolCalls);
