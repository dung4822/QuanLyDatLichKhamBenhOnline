using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Infrastructure.AI;

// Cài đặt ILlmClient bằng DeepSeek. Muốn đổi nhà cung cấp chỉ cần viết class khác cài ILlmClient.
public class DeepSeekClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly DeepSeekSettings _settings;

    public DeepSeekClient(
        HttpClient httpClient,
        DeepSeekSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    private const string Endpoint = "https://api.deepseek.com/chat/completions";

    // DeepSeek dùng snake_case và không cần các trường null (ví dụ "tools" khi không có công cụ).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<LlmReply> CompleteAsync(IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new LlmUnavailableException("Chưa cấu hình DeepSeek:ApiKey trong User Secrets.");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey.Trim());
        request.Content = JsonContent.Create(BuildRequest(messages, tools), options: JsonOptions);

        DeepSeekResponse? body;
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ProviderFailure(response.StatusCode);

            body = await response.Content.ReadFromJsonAsync<DeepSeekResponse>(JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Người gọi không hủy mà vẫn bị hủy → HttpClient hết thời gian chờ.
            throw new LlmUnavailableException("DeepSeek phản hồi quá thời gian chờ. Hãy thử lại.");
        }
        catch (HttpRequestException)
        {
            throw new LlmUnavailableException("Không kết nối được DeepSeek. Kiểm tra kết nối mạng.");
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }

        return ToReply(body);
    }

    private DeepSeekRequest BuildRequest(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmToolDefinition> tools)
    {
        var hasTools = tools.Count > 0;
        return new DeepSeekRequest(
            Model: _settings.Model,
            Messages: messages.Select(ToDeepSeekMessage).ToList(),
            MaxTokens: Math.Clamp(_settings.MaxOutputTokens, 128, 2048),
            Stream: false,
            Thinking: new DeepSeekThinking("disabled"), // Tắt chế độ suy luận: nhanh và rẻ hơn.
            Tools: hasTools ? tools.Select(ToDeepSeekTool).ToList() : null,
            ToolChoice: hasTools ? "auto" : null);
    }

    private static DeepSeekMessage ToDeepSeekMessage(LlmMessage message) => new(
        message.Role,
        message.Content,
        message.ToolCalls?
            .Select(call => new DeepSeekToolCall(call.Id, "function", new DeepSeekFunctionCall(call.Name, call.Arguments)))
            .ToList(),
        message.ToolCallId);

    private static DeepSeekTool ToDeepSeekTool(LlmToolDefinition tool)
        => new("function", new DeepSeekFunctionDefinition(tool.Name, tool.Description, tool.Parameters));

    private static LlmReply ToReply(DeepSeekResponse? body)
    {
        var choice = body?.Choices?.FirstOrDefault();
        if (choice?.Message is null)
            throw InvalidResponse();

        // "length" = bị cắt giữa chừng vì chạm max_tokens; nội dung hoặc tham số công cụ có thể dở dang.
        if (choice.FinishReason == "length")
            throw new LlmUnavailableException("DeepSeek trả về câu trả lời chưa đầy đủ. Hãy thử yêu cầu ngắn hơn.");

        var message = choice.Message;
        var toolCalls = (message.ToolCalls ?? [])
            .Select(call => new LlmToolCall(call.Id, call.Function.Name, call.Function.Arguments))
            .ToList();
        if (toolCalls.Count == 0 && string.IsNullOrWhiteSpace(message.Content))
            throw InvalidResponse();

        // Các trường khác như reasoning_content không được khai báo trong record nên tự bị bỏ qua.
        return new LlmReply(message.Content, toolCalls);
    }

    private static LlmUnavailableException InvalidResponse()
        => new("DeepSeek trả về dữ liệu không hợp lệ hoặc không có nội dung.");

    private static LlmUnavailableException ProviderFailure(HttpStatusCode status)
    {
        // Không đưa body thô của nhà cung cấp hoặc API key vào thông báo lỗi.
        var detail = status switch
        {
            HttpStatusCode.TooManyRequests => "DeepSeek đang giới hạn lượt gọi. Hãy thử lại sau.",
            HttpStatusCode.PaymentRequired => "Tài khoản DeepSeek không đủ số dư. Kiểm tra tài khoản API.",
            HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => "DeepSeek từ chối yêu cầu. Kiểm tra API key, quyền truy cập và cấu hình model.",
            HttpStatusCode.NotFound => "Không tìm thấy model DeepSeek. Kiểm tra DeepSeek:Model.",
            _ => "DeepSeek tạm thời không xử lý được yêu cầu."
        };
        return new LlmUnavailableException($"{detail} (DeepSeek HTTP {(int)status}).");
    }
}
