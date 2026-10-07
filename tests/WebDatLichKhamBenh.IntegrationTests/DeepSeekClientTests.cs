using System.Net;
using System.Text;
using System.Text.Json;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Infrastructure.AI;

namespace WebDatLichKhamBenh.IntegrationTests;

public class DeepSeekClientTests
{
    private const string TextResponse = """{"choices":[{"finish_reason":"stop","message":{"content":"Xin chào","reasoning_content":"private reasoning"}}]}""";

    [Fact]
    public async Task Sends_official_nonthinking_request_with_tool_definitions_and_results()
    {
        using var handler = new CaptureHandler(HttpStatusCode.OK, TextResponse);
        using var httpClient = new HttpClient(handler);
        var client = new DeepSeekClient(httpClient, new DeepSeekSettings { ApiKey = "test-key", MaxOutputTokens = 9999 });
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"specialtyId":{"type":"integer"}},"required":["specialtyId"],"additionalProperties":false}""");
        var call = new LlmToolCall("call-1", "find_doctors", "{\"specialtyId\":1}");

        var reply = await client.CompleteAsync(
            [new LlmMessage("system", "Use the clinic data."), new LlmMessage("user", "Tìm bác sĩ"),
             new LlmMessage("assistant", null, [call]), new LlmMessage("tool", "{\"doctors\":[]}", ToolCallId: "call-1")],
            [new LlmToolDefinition("find_doctors", "Find clinic doctors", schema.RootElement)]);

        Assert.Equal("Xin chào", reply.Content);
        Assert.Empty(reply.ToolCalls);
        Assert.Equal("https://api.deepseek.com/chat/completions", handler.Uri!.AbsoluteUri);
        Assert.Equal("Bearer test-key", handler.Authorization);
        var payload = handler.Payload;
        Assert.Equal("deepseek-flash", payload.GetProperty("model").GetString());
        Assert.Equal("disabled", payload.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(payload.GetProperty("stream").GetBoolean());
        Assert.Equal(2048, payload.GetProperty("max_tokens").GetInt32());
        Assert.Equal("auto", payload.GetProperty("tool_choice").GetString());
        var function = payload.GetProperty("tools")[0].GetProperty("function");
        Assert.Equal("find_doctors", function.GetProperty("name").GetString());
        Assert.Equal("object", function.GetProperty("parameters").GetProperty("type").GetString());
        var messages = payload.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.False(messages[2].TryGetProperty("content", out _));
        Assert.Equal("call-1", messages[2].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("{\"specialtyId\":1}", messages[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal("call-1", messages[3].GetProperty("tool_call_id").GetString());
        Assert.DoesNotContain("private reasoning", reply.Content!);
    }

    [Fact]
    public async Task Omits_tools_for_simple_reply_and_clamps_small_token_limit()
    {
        using var handler = new CaptureHandler(HttpStatusCode.OK, TextResponse);
        using var httpClient = new HttpClient(handler);
        var client = new DeepSeekClient(httpClient, new DeepSeekSettings { ApiKey = "test-key", MaxOutputTokens = 0 });
        await client.CompleteAsync([new LlmMessage("user", "Hello")], []);
        Assert.False(handler.Payload.TryGetProperty("tools", out _));
        Assert.False(handler.Payload.TryGetProperty("tool_choice", out _));
        Assert.Equal(128, handler.Payload.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Parses_tool_calls_without_exposing_reasoning()
    {
        var response = ToolResponse(new { id = "call-1", type = "function", function = new { name = "find_slots", arguments = "{\"doctorId\":5}" } });
        using var handler = new CaptureHandler(HttpStatusCode.OK, response);
        using var httpClient = new HttpClient(handler);
        var reply = await CreateClient(httpClient).CompleteAsync([new LlmMessage("user", "Tìm giờ khám")], []);
        Assert.Null(reply.Content);
        var call = Assert.Single(reply.ToolCalls);
        Assert.Equal(new LlmToolCall("call-1", "find_slots", "{\"doctorId\":5}"), call);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"choices\":null}")]
    [InlineData("{\"choices\":{}}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[null]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":null}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":42}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\" \"}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"tool_calls\":[]}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"tool_calls\":{}}}]}")]
    public async Task Malformed_or_empty_provider_response_returns_safe_error(string response)
    {
        using var handler = new CaptureHandler(HttpStatusCode.OK, response);
        using var httpClient = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => CreateClient(httpClient).CompleteAsync([], []));
        Assert.Contains("không hợp lệ", error.Message);
        Assert.DoesNotContain("test-key", error.Message);
    }

    [Theory]
    [InlineData("stop", "A partial answer")]
    [InlineData("tool_calls", null)]
    public async Task Rejects_truncated_text_and_tool_requests(string intendedReason, string? content)
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[] { new { finish_reason = "length", message = new
            {
                content,
                tool_calls = intendedReason == "tool_calls"
                    ? new[] { new { id = "call-1", type = "function", function = new { name = "find_slots", arguments = "{\"doctorId\":" } } }
                    : null
            } } }
        });
        using var handler = new CaptureHandler(HttpStatusCode.OK, body);
        using var httpClient = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => CreateClient(httpClient).CompleteAsync([], []));
        Assert.Contains("chưa đầy đủ", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "giới hạn")]
    [InlineData(HttpStatusCode.PaymentRequired, "số dư")]
    [InlineData(HttpStatusCode.Unauthorized, "API key")]
    [InlineData(HttpStatusCode.InternalServerError, "tạm thời")]
    public async Task Maps_provider_status_without_raw_body(HttpStatusCode status, string expected)
    {
        using var handler = new CaptureHandler(status, "secret-provider-body test-key");
        using var httpClient = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => CreateClient(httpClient).CompleteAsync([], []));
        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("secret-provider-body", error.Message);
        Assert.DoesNotContain("test-key", error.Message);
    }

    [Fact]
    public async Task Missing_key_does_not_send_a_request()
    {
        using var handler = new CaptureHandler(HttpStatusCode.OK, TextResponse);
        using var httpClient = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => new DeepSeekClient(httpClient, new DeepSeekSettings()).CompleteAsync([], []));
        Assert.Contains("DeepSeek:ApiKey", error.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_sending_request()
    {
        using var handler = new CaptureHandler(HttpStatusCode.OK, TextResponse);
        using var httpClient = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(httpClient).CompleteAsync([], [], cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_during_request_is_not_reported_as_provider_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new CancelHandler(cancellation);
        using var httpClient = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(httpClient).CompleteAsync([], [], cancellation.Token));
    }

    [Fact]
    public async Task Timeout_maps_to_unavailability()
    {
        using var handler = new FailureHandler(new TaskCanceledException());
        using var httpClient = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => CreateClient(httpClient).CompleteAsync([], []));
        Assert.Contains("thời gian chờ", error.Message);
    }

    [Fact]
    public async Task Network_error_maps_to_safe_unavailability()
    {
        using var handler = new FailureHandler(new HttpRequestException("private network detail"));
        using var httpClient = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => CreateClient(httpClient).CompleteAsync([], []));
        Assert.Contains("Không kết nối", error.Message);
        Assert.DoesNotContain("private network detail", error.Message);
    }

    private static DeepSeekClient CreateClient(HttpClient httpClient)
        => new(httpClient, new DeepSeekSettings { ApiKey = "test-key" });

    private static string ToolResponse(params object[] calls)
        => JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "tool_calls", message = new
        { content = (string?)null, reasoning_content = "private reasoning", tool_calls = calls } } } });

    private sealed class CaptureHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public JsonElement Payload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Payload = json.RootElement.Clone();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FailureHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CancelHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Expected cancellation.");
        }
    }
}
