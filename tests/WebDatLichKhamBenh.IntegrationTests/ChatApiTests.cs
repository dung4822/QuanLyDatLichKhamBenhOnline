using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebDatLichKhamBenh.Api.Security;
using WebDatLichKhamBenh.Api.Services;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.IntegrationTests;

// Test luồng chat với AI, kho lưu chat và công cụ thao tác giả lập: không tốn quota, không cần PostgreSQL.
// Luồng xác nhận thật với database nằm ở ChatActionApiTests.
public class ChatApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Doctor", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    [InlineData("Patient", HttpStatusCode.Created)]
    public async Task ChatRequiresPatient(string? role, HttpStatusCode expectedStatus)
    {
        await using var app = new ChatApp();
        using var client = await app.CreateClientAsync(role);

        var response = await client.PostAsync("/api/chats/sessions", null);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task MissingCsrfDoesNotCallLlm()
    {
        await using var app = new ChatApp();
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");

        var response = await client.PostAsJsonAsync($"/api/chats/sessions/{sessionId}/messages", new { message = "Xin chào" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Llm.Calls);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [new { message = "   " }];
        yield return [new { message = new string('a', 4001) }];
        yield return [new { message = "Xin chào", clientMessageId = Guid.Empty }];
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidInputIsRejectedBeforeLlm(object request)
    {
        await using var app = new ChatApp();
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync($"/api/chats/sessions/{sessionId}/messages", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Llm.Calls);
        Assert.Empty(app.Chats.Turns);
    }

    [Fact]
    public async Task AnotherPatientsSessionIsNotFound()
    {
        await using var app = new ChatApp();
        using var owner = await app.CreateClientAsync("Patient", patientId: 1);
        using var stranger = await app.CreateClientAsync("Patient", patientId: 2);
        var sessionId = await CreateSessionAsync(owner);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/chats/sessions/{sessionId}")).StatusCode);
        var send = await stranger.PostAsJsonAsync($"/api/chats/sessions/{sessionId}/messages", new { message = "Xin chào" });
        Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
        Assert.Empty(app.Llm.Calls);
    }

    [Fact]
    public async Task SessionListOnlyContainsTheCurrentPatientsSessions()
    {
        await using var app = new ChatApp();
        using var owner = await app.CreateClientAsync("Patient", patientId: 1);
        using var stranger = await app.CreateClientAsync("Patient", patientId: 2);
        var ownSessionId = await CreateSessionAsync(owner);
        await CreateSessionAsync(stranger);

        var sessions = await owner.GetFromJsonAsync<List<ChatSessionDto>>("/api/chats/sessions", Json);

        Assert.Equal(ownSessionId, Assert.Single(sessions!).ChatSessionId);
        Assert.Empty(app.Llm.Calls);
    }

    [Fact]
    public async Task ToolResultsAndSavedConversationReachLlm()
    {
        await using var app = new ChatApp(
            new LlmReply("Tôi có thể giúp gì?", []),
            new LlmReply(null, [new LlmToolCall("lookup-1", "get_specialties", "{}")]),
            new LlmReply("Có chuyên khoa Da liễu.", []));
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);
        await SendAsync(client, sessionId, "Xin chào");

        var result = await SendAsync(client, sessionId, "Có chuyên khoa nào?");

        Assert.Equal("Có chuyên khoa Da liễu.", result.Reply);
        var source = Assert.Single(result.Sources);
        Assert.Equal("get_specialties", source.Tool);
        Assert.Equal(3, app.Llm.Calls.Count);
        // Lượt trước được lấy từ DB (không phải do client gửi) và đưa vào ngữ cảnh.
        var secondTurn = app.Llm.Calls[1].Messages;
        Assert.Equal("system", secondTurn[0].Role);
        Assert.Equal(["user", "assistant", "user"], secondTurn.Skip(1).Select(x => x.Role));
        Assert.Equal("Xin chào", secondTurn[1].Content);
        Assert.Contains(app.Llm.Calls[1].Tools, tool => tool.Name == "get_specialties");
        Assert.Contains(app.Llm.Calls[1].Tools, tool => tool.Name == "propose_book_appointment");
        var toolMessage = Assert.Single(app.Llm.Calls[2].Messages.Where(x => x.Role == "tool"));
        Assert.Equal("lookup-1", toolMessage.ToolCallId);

        var detail = await client.GetFromJsonAsync<ChatSessionDetailDto>($"/api/chats/sessions/{sessionId}", Json);
        Assert.Equal(["Xin chào", "Có chuyên khoa nào?"], detail!.Turns.Select(x => x.Message));
        Assert.Equal("Xin chào", detail.Session.Title);
    }

    [Fact]
    public async Task OnlyTheLatestFiveTurnsAreSentToLlm()
    {
        await using var app = new ChatApp();
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);
        for (var i = 0; i < 7; i++)
            await SendAsync(client, sessionId, $"Tin {i}");

        await SendAsync(client, sessionId, "Tin mới");

        var messages = app.Llm.Calls[^1].Messages;
        Assert.Equal(1 + 5 * 2 + 1, messages.Count);
        Assert.Equal("Tin 2", messages[1].Content);
        Assert.Equal("Tin mới", messages[^1].Content);
        Assert.Equal(8, app.Chats.Turns.Count);
    }

    [Fact]
    public async Task RetryWithSameClientMessageIdReturnsSavedReplyWithoutCallingLlmAgain()
    {
        await using var app = new ChatApp(new LlmReply("Câu trả lời đầu tiên", []));
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);
        var clientMessageId = Guid.NewGuid();

        var first = await SendAsync(client, sessionId, "Xin chào", clientMessageId);
        var retry = await SendAsync(client, sessionId, "Xin chào", clientMessageId);

        Assert.Equal(first.ChatTurnId, retry.ChatTurnId);
        Assert.Equal("Câu trả lời đầu tiên", retry.Reply);
        Assert.Single(app.Llm.Calls);
        Assert.Single(app.Chats.Turns);
    }

    [Fact]
    public async Task SameClientMessageIdInDifferentSessionsDoesNotReuseAnotherConversationsReply()
    {
        await using var app = new ChatApp(
            new LlmReply("Câu trả lời cuộc trò chuyện thứ nhất", []),
            new LlmReply("Câu trả lời cuộc trò chuyện thứ hai", []));
        using var client = await app.CreateClientAsync("Patient");
        var firstSessionId = await CreateSessionAsync(client);
        var secondSessionId = await CreateSessionAsync(client);
        var clientMessageId = Guid.NewGuid();

        var first = await SendAsync(client, firstSessionId, "Tin nhắn riêng thứ nhất", clientMessageId);
        var second = await SendAsync(client, secondSessionId, "Tin nhắn riêng thứ hai", clientMessageId);

        Assert.Equal(firstSessionId, first.ChatSessionId);
        Assert.Equal(secondSessionId, second.ChatSessionId);
        Assert.NotEqual(first.ChatTurnId, second.ChatTurnId);
        Assert.Equal("Câu trả lời cuộc trò chuyện thứ hai", second.Reply);
        Assert.Equal(2, app.Chats.Turns.Count);
        Assert.Equal(2, app.Llm.Calls.Count);
        Assert.DoesNotContain(app.Llm.Calls[1].Messages, message => message.Content == "Tin nhắn riêng thứ nhất");
    }

    [Fact]
    public async Task ProposalIsSavedWithTurnAndReplacesOlderPendingProposal()
    {
        var propose = new LlmToolCall("propose-1", "propose_book_appointment", """{"appointmentSlotId":1}""");
        var proposeAgain = new LlmToolCall("propose-2", "propose_book_appointment", """{"appointmentSlotId":2}""");
        await using var app = new ChatApp(
            new LlmReply(null, [propose]), new LlmReply("Bạn kiểm tra thẻ và bấm Xác nhận nhé.", []),
            new LlmReply(null, [proposeAgain]), new LlmReply("Mình đã đổi sang giờ khác.", []));
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);

        var first = await SendAsync(client, sessionId, "Đặt giờ thứ nhất");
        var second = await SendAsync(client, sessionId, "Đổi sang giờ thứ hai");

        Assert.Equal(ChatActionStatus.Pending, first.Action!.Status);
        Assert.Equal("Đặt lịch khám", first.Action.Title);
        Assert.Equal(ChatActionStatus.Pending, second.Action!.Status);
        var detail = await client.GetFromJsonAsync<ChatSessionDetailDto>($"/api/chats/sessions/{sessionId}", Json);
        Assert.Equal(ChatActionStatus.Superseded, detail!.Turns[0].Action!.Status);
        Assert.Equal(ChatActionStatus.Pending, detail.Turns[1].Action!.Status);
        // Lượt sau, mô hình được báo trạng thái thật của đề xuất trước.
        Assert.Contains("CHƯA thực hiện", app.Llm.Calls[2].Messages[0].Content);
    }

    [Fact]
    public async Task RetryingAnOlderMessageReturnsItsSupersededActionWithoutReplacingTheNewProposal()
    {
        await using var app = new ChatApp(
            new LlmReply(null, [new LlmToolCall("first", "propose_book_appointment", """{"appointmentSlotId":1}""")]),
            new LlmReply("Bạn kiểm tra thẻ thứ nhất nhé.", []),
            new LlmReply(null, [new LlmToolCall("second", "propose_book_appointment", """{"appointmentSlotId":2}""")]),
            new LlmReply("Bạn kiểm tra thẻ mới nhé.", []));
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);
        var firstMessageId = Guid.NewGuid();
        var first = await SendAsync(client, sessionId, "Đặt giờ thứ nhất", firstMessageId);
        var second = await SendAsync(client, sessionId, "Đổi sang giờ thứ hai");

        var retry = await SendAsync(client, sessionId, "Đặt giờ thứ nhất", firstMessageId);

        Assert.Equal(first.ChatTurnId, retry.ChatTurnId);
        Assert.Equal(first.Action!.ChatActionId, retry.Action!.ChatActionId);
        Assert.Equal(ChatActionStatus.Superseded, retry.Action.Status);
        var pending = Assert.Single(app.Chats.Turns.Where(turn => turn.Action?.Status == ChatActionStatus.Pending));
        Assert.Equal(second.Action!.ChatActionId, pending.Action!.ChatActionId);
        Assert.Equal(2, app.Chats.Turns.Count);
        Assert.Equal(4, app.Llm.Calls.Count);
    }

    [Fact]
    public async Task MultipleProposalsInOneReplyOnlySaveTheLastConfirmationCard()
    {
        const string lastPayload = """{"appointmentSlotId":2}""";
        await using var app = new ChatApp(
            new LlmReply(null,
            [
                new LlmToolCall("first", "propose_book_appointment", """{"appointmentSlotId":1}"""),
                new LlmToolCall("second", "propose_book_appointment", lastPayload)
            ]),
            new LlmReply("Bạn kiểm tra giờ cuối cùng trên thẻ nhé.", []));
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);

        var result = await SendAsync(client, sessionId, "Đặt giờ thứ hai giúp tôi");

        var saved = Assert.Single(app.Chats.Turns);
        Assert.NotNull(saved.Action);
        Assert.Equal(lastPayload, saved.Action.PayloadJson);
        Assert.Equal(saved.Action.ChatActionId, result.Action!.ChatActionId);
        Assert.Equal(ChatActionStatus.Pending, result.Action.Status);
    }

    public static IEnumerable<object?[]> InvalidAssistantReplies()
    {
        yield return [null];
        yield return ["   "];
        yield return [new string('a', 4001)];
    }

    [Theory]
    [MemberData(nameof(InvalidAssistantReplies))]
    public async Task InvalidAssistantReplyDoesNotSaveItsProposalOrReplaceAnExistingPendingCard(string? reply)
    {
        await using var app = new ChatApp(
            new LlmReply(null, [new LlmToolCall("first", "propose_book_appointment", """{"appointmentSlotId":1}""")]),
            new LlmReply("Bạn kiểm tra thẻ nhé.", []),
            new LlmReply(null, [new LlmToolCall("second", "propose_book_appointment", """{"appointmentSlotId":2}""")]),
            new LlmReply(reply, []));
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);
        var first = await SendAsync(client, sessionId, "Đặt giờ thứ nhất");

        var response = await client.PostAsJsonAsync($"/api/chats/sessions/{sessionId}/messages",
            new { message = "Đổi sang giờ thứ hai" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var saved = Assert.Single(app.Chats.Turns);
        Assert.Equal(first.ChatTurnId, saved.ChatTurnId);
        Assert.Equal(first.Action!.ChatActionId, saved.Action!.ChatActionId);
        Assert.Equal(ChatActionStatus.Pending, saved.Action.Status);
    }

    [Fact]
    public async Task UnsupportedWriteToolIsNotExecuted()
    {
        await using var app = new ChatApp(
            new LlmReply(null, [new LlmToolCall("write-1", "create_appointment", "{}")]),
            new LlmReply("Mình chỉ có thể tạo thẻ xác nhận.", []));
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);

        var result = await SendAsync(client, sessionId, "Đặt lịch giúp tôi");

        Assert.Null(result.Action);
        Assert.Equal(0, app.Lookup.SupportedCalls);
        Assert.DoesNotContain(app.Llm.Calls[0].Tools, tool => tool.Name == "create_appointment");
        Assert.Contains(app.Llm.Calls[1].Messages, x => x.Role == "tool" && x.Content!.Contains("error"));
    }

    [Theory]
    [InlineData(1, 5, 6)]
    [InlineData(3, 6, 3)]
    public async Task RepeatedToolRequestsStayWithinModelAndToolBudgets(int toolsPerReply,
        int expectedLookups, int expectedModelCalls)
    {
        var replies = Enumerable.Range(0, 6).Select(round => new LlmReply(null,
            Enumerable.Range(0, toolsPerReply)
                .Select(index => new LlmToolCall($"lookup-{round}-{index}", "get_specialties", "{}"))
                .ToArray())).ToArray();
        await using var app = new ChatApp(replies);
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);

        var result = await SendAsync(client, sessionId, "Tra cứu thêm");

        Assert.Equal(expectedModelCalls, app.Llm.Calls.Count);
        Assert.Equal(expectedLookups, app.Lookup.SupportedCalls);
        Assert.Equal(expectedLookups, result.Sources.Count);
        Assert.False(string.IsNullOrWhiteSpace(result.Reply));
    }

    [Fact]
    public async Task TooManyToolCallsInOneReplyAreNotExecuted()
    {
        var calls = Enumerable.Range(0, 9).Select(i => new LlmToolCall($"lookup-{i}", "get_specialties", "{}")).ToArray();
        await using var app = new ChatApp(new LlmReply(null, calls));
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);

        await SendAsync(client, sessionId, "Tra cứu tất cả");

        Assert.Single(app.Llm.Calls);
        Assert.Equal(0, app.Lookup.SupportedCalls);
    }

    [Fact]
    public async Task RateLimitRejectsTheEleventhMessageBeforeCallingLlm()
    {
        await using var app = new ChatApp();
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);
        for (var i = 0; i < 10; i++)
            await SendAsync(client, sessionId, "Xin chào");

        var limited = await client.PostAsJsonAsync($"/api/chats/sessions/{sessionId}/messages", new { message = "Xin chào" });

        await ApiErrorFormatTests.AssertProblemAsync(limited, 429, "Gửi quá nhiều yêu cầu.");
        Assert.Equal(10, app.Llm.Calls.Count);
    }

    [Fact]
    public async Task ProviderFailureReturnsBadGatewayAndSavesNothing()
    {
        var failure = new LlmUnavailableException("Dịch vụ AI tạm thời không khả dụng.");
        failure.Data["ProviderBody"] = "sensitive-provider-error";
        await using var app = new ChatApp { Failure = failure };
        using var client = await app.CreateClientAsync("Patient");
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync($"/api/chats/sessions/{sessionId}/messages", new { message = "Xin chào" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(failure.Message, problem!.Detail);
        Assert.DoesNotContain("sensitive-provider-error", await response.Content.ReadAsStringAsync());
        Assert.Empty(app.Chats.Turns);
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/chats/sessions", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChatSessionDto>(Json))!.ChatSessionId;
    }

    private static async Task<ChatResponse> SendAsync(HttpClient client, Guid sessionId, string message,
        Guid? clientMessageId = null)
    {
        var response = await client.PostAsJsonAsync($"/api/chats/sessions/{sessionId}/messages",
            new { message, clientMessageId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChatResponse>(Json))!;
    }

    // Ứng dụng thật, chỉ thay AI, kho lưu chat, công cụ và đăng nhập bằng bản giả lập.
    private sealed class ChatApp : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        public ScriptedLlm Llm { get; }
        public InMemoryChatRepository Chats { get; } = new();
        public LookupStub Lookup { get; } = new();
        public Exception? Failure { init => Llm.Failure = value; }

        public ChatApp(params LlmReply[] replies)
        {
            Llm = new ScriptedLlm(replies);
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    // Test dùng khóa trong bộ nhớ, không phụ thuộc key ring/DPAPI của tài khoản Windows.
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    var worker = services.Single(x => x.ServiceType == typeof(IHostedService) &&
                        x.ImplementationType == typeof(AppointmentSlotReconciliationService));
                    services.Remove(worker);
                    services.RemoveAll<ILlmClient>();
                    services.RemoveAll<IChatLookupService>();
                    services.RemoveAll<IChatActionService>();
                    services.RemoveAll<IChatRepository>();
                    services.AddSingleton<ILlmClient>(Llm);
                    services.AddSingleton<IChatLookupService>(Lookup);
                    services.AddSingleton<IChatActionService>(new ActionStub());
                    services.AddSingleton<IChatRepository>(Chats);
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                });
            });
        }

        public async Task<HttpClient> CreateClientAsync(string? role, int patientId = 1)
        {
            var client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Patient", patientId.ToString());
            await AppointmentApiFixture.RefreshCsrfTokenAsync(client);
            return client;
        }

        public ValueTask DisposeAsync() => _factory.DisposeAsync();
    }

    private sealed class ScriptedLlm(params LlmReply[] replies) : ILlmClient
    {
        private readonly Queue<LlmReply> _replies = new(replies);
        public List<(IReadOnlyList<LlmMessage> Messages, IReadOnlyList<LlmToolDefinition> Tools)> Calls { get; } = [];
        public Exception? Failure { get; set; }

        public Task<LlmReply> CompleteAsync(IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<LlmToolDefinition> tools, CancellationToken cancellationToken = default)
        {
            Calls.Add((messages.ToArray(), tools.ToArray()));
            if (Failure is not null) throw Failure;
            return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : new LlmReply("Xin chào", []));
        }
    }

    private sealed class LookupStub : IChatLookupService
    {
        public int SupportedCalls { get; private set; }
        public IReadOnlyList<LlmToolDefinition> Tools { get; } =
        [new("get_specialties", "Tra cứu chuyên khoa", JsonSerializer.SerializeToElement(new { type = "object", properties = new { }, additionalProperties = false }))];

        public Task<ChatLookupResult> ExecuteAsync(LlmToolCall toolCall, CancellationToken cancellationToken = default)
        {
            if (toolCall.Name != "get_specialties")
                return Task.FromResult(new ChatLookupResult(toolCall.Name,
                    JsonSerializer.SerializeToElement(new { error = "Công cụ không được hỗ trợ." })));
            SupportedCalls++;
            return Task.FromResult(new ChatLookupResult(toolCall.Name,
                JsonSerializer.SerializeToElement(new { items = new[] { new { id = 1, name = "Da liễu" } } })));
        }
    }

    // Tạo đề xuất giả; kiểm tra nghiệp vụ thật nằm trong ChatActionApiTests.
    private sealed class ActionStub : IChatActionService
    {
        public IReadOnlyList<LlmToolDefinition> Tools { get; } =
        [new("propose_book_appointment", "Tạo thẻ xác nhận đặt lịch", JsonSerializer.SerializeToElement(new { type = "object" }))];

        public bool CanHandle(string toolName) => toolName == "propose_book_appointment";

        public Task<ChatToolResult> ExecuteAsync(LlmToolCall toolCall, CancellationToken cancellationToken = default)
        {
            var preview = new ChatActionPreview("Đặt lịch khám", [new("Bác sĩ", null, "Bác sĩ thử nghiệm")]);
            var action = new ChatAction
            {
                Type = ChatActionType.BookAppointment,
                PayloadJson = toolCall.Arguments,
                PreviewJson = JsonSerializer.Serialize(preview),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            var result = new ChatLookupResult(toolCall.Name,
                JsonSerializer.SerializeToElement(new { status = "awaiting_user_confirmation" }));
            return Task.FromResult(new ChatToolResult(result, action));
        }

        public Task<ChatActionDto> ConfirmAsync(Guid actionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChatActionDto> RejectAsync(Guid actionId) => throw new NotSupportedException();
    }

    // Kho lưu chat trong bộ nhớ, mô phỏng đúng các hành vi ChatRepository cần có.
    private sealed class InMemoryChatRepository : IChatRepository
    {
        private long _nextTurnId = 1;
        public List<ChatSession> Sessions { get; } = [];
        public List<ChatTurn> Turns { get; } = [];
        private IEnumerable<ChatAction> Actions => Turns.Select(x => x.Action).OfType<ChatAction>();

        public Task AddSessionAsync(ChatSession session)
        {
            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<ChatSession?> GetSessionAsync(Guid sessionId, int patientId, bool tracking = false)
            => Task.FromResult(Sessions.FirstOrDefault(x => x.ChatSessionId == sessionId && x.PatientId == patientId));

        public Task<List<ChatSession>> GetSessionsAsync(int patientId, int take)
            => Task.FromResult(Sessions.Where(x => x.PatientId == patientId)
                .OrderByDescending(x => x.UpdatedAt).Take(take).ToList());

        public Task<List<ChatTurn>> GetRecentTurnsAsync(Guid sessionId, int take)
            => Task.FromResult(Turns.Where(x => x.ChatSessionId == sessionId)
                .OrderByDescending(x => x.ChatTurnId).Take(take).Reverse().ToList());

        public Task<ChatTurn?> GetTurnByClientMessageIdAsync(Guid sessionId, Guid clientMessageId)
            => Task.FromResult(Turns.FirstOrDefault(x => x.ChatSessionId == sessionId && x.ClientMessageId == clientMessageId));

        public Task<ChatAction?> GetActionAsync(Guid actionId, int patientId, bool tracking = false)
            => Task.FromResult(Actions.FirstOrDefault(x => x.ChatActionId == actionId &&
                Sessions.Any(s => s.ChatSessionId == x.ChatTurn.ChatSessionId && s.PatientId == patientId)));

        public Task<bool> TryCompleteAsync(Guid actionId, ChatActionStatus status, string? failureReason = null)
        {
            var action = Actions.FirstOrDefault(x => x.ChatActionId == actionId && x.Status == ChatActionStatus.Pending);
            if (action is null) return Task.FromResult(false);
            action.Status = status;
            action.FailureReason = failureReason;
            action.CompletedAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task SupersedePendingAsync(Guid sessionId, Guid keepActionId)
        {
            foreach (var action in Actions.Where(x => x.ChatTurn.ChatSessionId == sessionId &&
                         x.Status == ChatActionStatus.Pending && x.ChatActionId != keepActionId))
                action.Status = ChatActionStatus.Superseded;
            return Task.CompletedTask;
        }

        public Task<bool> TryAddTurnAsync(ChatTurn turn)
        {
            if (Turns.Any(x => x.ChatSessionId == turn.ChatSessionId && x.ClientMessageId == turn.ClientMessageId))
                return Task.FromResult(false);
            turn.ChatTurnId = _nextTurnId++;
            if (turn.Action is not null)
            {
                turn.Action.ChatTurn = turn;
                turn.Action.ChatTurnId = turn.ChatTurnId;
            }
            Turns.Add(turn);
            return Task.FromResult(true);
        }

        public Task SaveChangesAsync() => Task.CompletedTask;
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ChatTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());
            // Rate limit chia theo NameIdentifier, nên mỗi người dùng giả lập phải giữ cố định một ID.
            var patientId = int.Parse(Request.Headers["X-Test-Patient"].ToString());
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, $"00000000-0000-0000-0000-{patientId:D12}"),
                new(ClaimTypes.Name, "chat-test-user"),
                new(ClaimTypes.Role, role.ToString())
            };
            if (role == "Patient")
                claims.Add(new Claim(ClinicClaims.PatientId, patientId.ToString()));
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
