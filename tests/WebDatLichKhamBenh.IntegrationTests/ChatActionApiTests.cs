using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.IntegrationTests;

// Luồng "AI đề xuất → người dùng xác nhận" với PostgreSQL thật và service nghiệp vụ thật.
// Chỉ AI là giả lập: test quyết định AI gọi công cụ nào, với tham số gì.
public class ChatActionApiTests(AppointmentApiFixture fixture) : IClassFixture<AppointmentApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private const string Password = "Patient-test-password-2026!";

    [Fact]
    public async Task Booking_happens_only_after_confirm_and_double_confirm_books_once()
    {
        var s = await fixture.SeedAsync();
        var (client, sessionId) = await StartChatAsync(s.PatientId);

        var proposal = await AskAsync(client, sessionId, "propose_book_appointment",
            new { appointmentSlotId = s.Slots[0], reason = "Đau đầu" });

        var action = proposal.Action!;
        Assert.Equal(ChatActionStatus.Pending, action.Status);
        Assert.Equal("Đặt lịch khám", action.Title);
        Assert.Contains(action.Details, x => x.Label == "Lý do khám" && x.Value == "Đau đầu");
        Assert.Equal(0, await CountActiveAppointmentsAsync(s.PatientId, s.Slots[0]));

        // Bấm hai lần cùng lúc (double click / retry): chỉ một lịch hẹn được tạo.
        var confirms = await Task.WhenAll(
            client.PostAsync($"/api/chats/actions/{action.ChatActionId}/confirm", null),
            client.PostAsync($"/api/chats/actions/{action.ChatActionId}/confirm", null));
        foreach (var response in confirms)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ChatActionStatus.Confirmed, (await ReadAsync<ChatActionDto>(response)).Status);
        }
        Assert.Equal(1, await CountActiveAppointmentsAsync(s.PatientId, s.Slots[0]));

        var reject = await client.PostAsync($"/api/chats/actions/{action.ChatActionId}/reject", null);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
    }

    [Fact]
    public async Task Confirm_audit_failure_rolls_back_booking_and_keeps_proposal_available_for_retry()
    {
        var s = await fixture.SeedAsync();
        var (client, sessionId) = await StartChatAsync(s.PatientId);
        using var patientClient = client;
        var action = (await AskAsync(client, sessionId, "propose_book_appointment",
            new { appointmentSlotId = s.Slots[0] })).Action!;

        fixture.Failures.FailNextHistorySave = true;
        var failed = await client.PostAsync($"/api/chats/actions/{action.ChatActionId}/confirm", null);

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using (var db = fixture.CreateDbContext())
        {
            var pending = await db.ChatActions.SingleAsync(x => x.ChatActionId == action.ChatActionId);
            Assert.Equal(ChatActionStatus.Pending, pending.Status);
            Assert.Null(pending.CompletedAt);
            Assert.Null(pending.ResultJson);
            Assert.Null(pending.FailureReason);
            Assert.False(await db.Appointments.AnyAsync(x => x.PatientId == s.PatientId));
            Assert.Equal(AppointmentSlotStatus.Available, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
        }
        Assert.Equal(ChatActionStatus.Pending, (await GetCardAsync(client, sessionId)).Status);

        var retry = await client.PostAsync($"/api/chats/actions/{action.ChatActionId}/confirm", null);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(ChatActionStatus.Confirmed, (await ReadAsync<ChatActionDto>(retry)).Status);
        await using (var db = fixture.CreateDbContext())
        {
            var appointment = Assert.Single(await db.Appointments.Where(x => x.PatientId == s.PatientId).ToListAsync());
            Assert.Equal(s.Slots[0], appointment.AppointmentSlotId);
            Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
            Assert.Equal(AppointmentSlotStatus.Booked, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
            var history = Assert.Single(await db.AppointmentHistories
                .Where(x => x.AppointmentId == appointment.AppointmentId).ToListAsync());
            Assert.Equal("Created", history.Action);
            var confirmed = await db.ChatActions.SingleAsync(x => x.ChatActionId == action.ChatActionId);
            Assert.Equal(ChatActionStatus.Confirmed, confirmed.Status);
            Assert.NotNull(confirmed.CompletedAt);
            using var result = JsonDocument.Parse(confirmed.ResultJson!);
            Assert.Equal(appointment.AppointmentId, result.RootElement.GetProperty("appointmentId").GetInt32());
        }
    }

    [Fact]
    public async Task Reject_retry_returns_the_same_result_without_booking()
    {
        var s = await fixture.SeedAsync();
        var (client, sessionId) = await StartChatAsync(s.PatientId);
        using var patientClient = client;
        var action = (await AskAsync(client, sessionId, "propose_book_appointment",
            new { appointmentSlotId = s.Slots[0] })).Action!;

        var first = await client.PostAsync($"/api/chats/actions/{action.ChatActionId}/reject", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var rejected = await ReadAsync<ChatActionDto>(first);
        Assert.Equal(ChatActionStatus.Rejected, rejected.Status);
        Assert.NotNull(rejected.CompletedAt);

        var retry = await client.PostAsync($"/api/chats/actions/{action.ChatActionId}/reject", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retried = await ReadAsync<ChatActionDto>(retry);
        Assert.Equal(rejected.ChatActionId, retried.ChatActionId);
        Assert.Equal(ChatActionStatus.Rejected, retried.Status);
        Assert.Equal(rejected.CompletedAt, retried.CompletedAt);
        Assert.Equal(ChatActionStatus.Rejected, (await GetCardAsync(client, sessionId)).Status);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync($"/api/chats/actions/{action.ChatActionId}/confirm", null)).StatusCode);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Appointments.AnyAsync(x => x.PatientId == s.PatientId));
        Assert.Equal(AppointmentSlotStatus.Available, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
    }

    [Theory]
    [InlineData("confirm", ChatActionStatus.Confirmed)]
    [InlineData("reject", ChatActionStatus.Rejected)]
    public async Task Action_requires_csrf_before_changing_proposal_or_appointment(string operation, ChatActionStatus expectedStatus)
    {
        var s = await fixture.SeedAsync();
        var (client, sessionId) = await StartChatAsync(s.PatientId);
        using var patientClient = client;
        var action = (await AskAsync(client, sessionId, "propose_book_appointment",
            new { appointmentSlotId = s.Slots[0] })).Action!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");

        var rejected = await client.PostAsync($"/api/chats/actions/{action.ChatActionId}/{operation}", null);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var pending = await GetCardAsync(client, sessionId);
        Assert.Equal(ChatActionStatus.Pending, pending.Status);
        Assert.Null(pending.CompletedAt);
        Assert.Equal(0, await CountActiveAppointmentsAsync(s.PatientId, s.Slots[0]));

        await AppointmentApiFixture.RefreshCsrfTokenAsync(client);
        var retry = await client.PostAsync($"/api/chats/actions/{action.ChatActionId}/{operation}", null);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(expectedStatus, (await ReadAsync<ChatActionDto>(retry)).Status);
        Assert.Equal(operation == "confirm" ? 1 : 0, await CountActiveAppointmentsAsync(s.PatientId, s.Slots[0]));
    }

    [Fact]
    public async Task Confirm_fails_and_card_shows_reason_when_slot_was_taken_meanwhile()
    {
        var s = await fixture.SeedAsync();
        var (client, sessionId) = await StartChatAsync(s.PatientId);
        var action = (await AskAsync(client, sessionId, "propose_book_appointment",
            new { appointmentSlotId = s.Slots[1] })).Action!;

        await BookByAdminAsync(s.OtherPatientId, s.Slots[1]);
        var confirm = await client.PostAsync($"/api/chats/actions/{action.ChatActionId}/confirm", null);

        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        var card = await GetCardAsync(client, sessionId);
        Assert.Equal(ChatActionStatus.Failed, card.Status);
        Assert.False(string.IsNullOrWhiteSpace(card.FailureReason));
        Assert.Equal(0, await CountActiveAppointmentsAsync(s.PatientId, s.Slots[1]));
    }

    [Fact]
    public async Task Expired_or_rejected_proposals_cannot_be_confirmed()
    {
        var s = await fixture.SeedAsync();
        var (client, sessionId) = await StartChatAsync(s.PatientId);

        var expired = (await AskAsync(client, sessionId, "propose_book_appointment",
            new { appointmentSlotId = s.Slots[0] })).Action!;
        await using (var db = fixture.CreateDbContext())
            await db.ChatActions.Where(x => x.ChatActionId == expired.ChatActionId)
                .ExecuteUpdateAsync(x => x.SetProperty(a => a.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync($"/api/chats/actions/{expired.ChatActionId}/confirm", null)).StatusCode);
        Assert.Equal(ChatActionStatus.Expired, (await GetCardAsync(client, sessionId)).Status);

        var rejected = (await AskAsync(client, sessionId, "propose_book_appointment",
            new { appointmentSlotId = s.Slots[0] })).Action!;
        var reject = await client.PostAsync($"/api/chats/actions/{rejected.ChatActionId}/reject", null);
        Assert.Equal(ChatActionStatus.Rejected, (await ReadAsync<ChatActionDto>(reject)).Status);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync($"/api/chats/actions/{rejected.ChatActionId}/confirm", null)).StatusCode);

        Assert.Equal(0, await CountActiveAppointmentsAsync(s.PatientId, s.Slots[0]));
    }

    [Fact]
    public async Task Other_users_cannot_read_confirm_or_reject_a_patients_proposal()
    {
        var s = await fixture.SeedAsync();
        var (client, sessionId) = await StartChatAsync(s.PatientId);
        var action = (await AskAsync(client, sessionId, "propose_book_appointment",
            new { appointmentSlotId = s.Slots[0] })).Action!;
        var (stranger, _) = await StartChatAsync(s.OtherPatientId);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/chats/sessions/{sessionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PostAsync($"/api/chats/actions/{action.ChatActionId}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PostAsync($"/api/chats/actions/{action.ChatActionId}/reject", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await fixture.Client.PostAsync($"/api/chats/actions/{action.ChatActionId}/confirm", null)).StatusCode);
        Assert.Equal(0, await CountActiveAppointmentsAsync(s.PatientId, s.Slots[0]));
    }

    [Fact]
    public async Task Reschedule_and_cancel_through_chat_use_the_normal_appointment_rules()
    {
        var s = await fixture.SeedAsync();
        var appointment = await BookByAdminAsync(s.PatientId, s.Slots[2]);
        var (client, sessionId) = await StartChatAsync(s.PatientId);

        var reschedule = (await AskAsync(client, sessionId, "propose_reschedule_appointment",
            new { appointmentId = appointment.AppointmentId, newAppointmentSlotId = s.Slots[3] })).Action!;
        Assert.Contains(reschedule.Details, x => x.Label == "Giờ khám" && x.Before != x.Value);
        Assert.Equal(s.Slots[2], (await GetAppointmentAsync(client, appointment.AppointmentId)).AppointmentSlotId);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/chats/actions/{reschedule.ChatActionId}/confirm", null)).StatusCode);
        Assert.Equal(s.Slots[3], (await GetAppointmentAsync(client, appointment.AppointmentId)).AppointmentSlotId);

        var cancel = (await AskAsync(client, sessionId, "propose_cancel_appointment",
            new { appointmentId = appointment.AppointmentId, reason = "Bận việc" })).Action!;
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/chats/actions/{cancel.ChatActionId}/confirm", null)).StatusCode);
        var cancelled = await GetAppointmentAsync(client, appointment.AppointmentId);
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.Equal("Bận việc", cancelled.CancellationReason);
    }

    [Fact]
    public async Task Tools_cannot_target_another_patients_appointment()
    {
        var s = await fixture.SeedAsync();
        var othersAppointment = await BookByAdminAsync(s.OtherPatientId, s.Slots[0]);
        var (client, sessionId) = await StartChatAsync(s.PatientId);

        var response = await AskAsync(client, sessionId, "propose_cancel_appointment",
            new { appointmentId = othersAppointment.AppointmentId });

        Assert.Null(response.Action);
        Assert.True(Assert.Single(response.Sources).Data.TryGetProperty("error", out _));
        Assert.Equal(AppointmentStatus.Confirmed, (await GetAppointmentByAdminAsync(othersAppointment.AppointmentId)).Status);
    }

    [Fact]
    public async Task My_appointments_tool_lists_only_the_current_patients_upcoming_appointments()
    {
        var s = await fixture.SeedAsync();
        var mine = await BookByAdminAsync(s.PatientId, s.Slots[0]);
        await BookByAdminAsync(s.OtherPatientId, s.Slots[1]);
        var (client, sessionId) = await StartChatAsync(s.PatientId);

        var response = await AskAsync(client, sessionId, "get_my_appointments", new { });

        var items = Assert.Single(response.Sources).Data.GetProperty("items");
        Assert.Equal(mine.AppointmentId, Assert.Single(items.EnumerateArray()).GetProperty("appointmentId").GetInt32());
    }

    [Fact]
    public async Task Profile_update_waits_for_confirm_and_refuses_to_overwrite_newer_changes()
    {
        var s = await fixture.SeedAsync();
        var (client, sessionId) = await StartChatAsync(s.PatientId);

        var update = (await AskAsync(client, sessionId, "propose_update_profile",
            new { phoneNumber = "0912345678", address = "Hà Nội" })).Action!;
        Assert.Contains(update.Details, x => x.Label == "Số điện thoại" && x.Before == "0901234567" && x.Value == "0912345678");
        Assert.Equal("0901234567", (await GetProfileAsync(client)).PhoneNumber);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/chats/actions/{update.ChatActionId}/confirm", null)).StatusCode);
        var profile = await GetProfileAsync(client);
        Assert.Equal("0912345678", profile.PhoneNumber);
        Assert.Equal("Hà Nội", profile.Address);

        // Hồ sơ bị sửa ở nơi khác sau khi tạo thẻ: không được ghi đè.
        var stale = (await AskAsync(client, sessionId, "propose_update_profile", new { phoneNumber = "0999999999" })).Action!;
        var manual = await client.PutAsJsonAsync("/api/me/profile", new
        {
            fullName = profile.FullName, dateOfBirth = profile.DateOfBirth, gender = profile.Gender,
            phoneNumber = "0988888888", address = profile.Address
        });
        Assert.Equal(HttpStatusCode.OK, manual.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync($"/api/chats/actions/{stale.ChatActionId}/confirm", null)).StatusCode);
        Assert.Equal("0988888888", (await GetProfileAsync(client)).PhoneNumber);
        Assert.Equal(ChatActionStatus.Failed, (await GetCardAsync(client, sessionId)).Status);
    }

    [Fact]
    public async Task New_proposal_replaces_older_pending_one_and_retry_does_not_duplicate_it()
    {
        var s = await fixture.SeedAsync();
        var (client, sessionId) = await StartChatAsync(s.PatientId);
        var first = (await AskAsync(client, sessionId, "propose_book_appointment",
            new { appointmentSlotId = s.Slots[0] })).Action!;

        var clientMessageId = Guid.NewGuid();
        var second = await AskAsync(client, sessionId, "propose_book_appointment",
            new { appointmentSlotId = s.Slots[1] }, clientMessageId);
        var callsBeforeRetry = fixture.Llm.CallCount;
        var retry = await SendAsync(client, sessionId, "Đặt lịch giúp mình", clientMessageId);

        Assert.Equal(callsBeforeRetry, fixture.Llm.CallCount);
        Assert.Equal(second.Action!.ChatActionId, retry.Action!.ChatActionId);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync($"/api/chats/actions/{first.ChatActionId}/confirm", null)).StatusCode);
        var detail = await GetSessionAsync(client, sessionId);
        Assert.Equal(2, detail.Turns.Count);
        Assert.Equal(ChatActionStatus.Superseded, detail.Turns[0].Action!.Status);
        Assert.Equal(ChatActionStatus.Pending, detail.Turns[1].Action!.Status);
    }

    // ===================== Tiện ích =====================

    private async Task<(HttpClient Client, Guid SessionId)> StartChatAsync(int patientId)
    {
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var created = await fixture.Client.PostAsJsonAsync("/api/accounts",
            new { email, password = Password, displayName = "Bệnh nhân chat", role = "Patient", patientId });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var client = await fixture.CreateClientWithCsrfAsync();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await AppointmentApiFixture.RefreshCsrfTokenAsync(client);

        var session = await client.PostAsync("/api/chats/sessions", null);
        Assert.Equal(HttpStatusCode.Created, session.StatusCode);
        return (client, (await ReadAsync<ChatSessionDto>(session)).ChatSessionId);
    }

    // AI giả lập gọi đúng một công cụ rồi trả lời bằng chữ.
    private async Task<ChatResponse> AskAsync(HttpClient client, Guid sessionId, string tool, object arguments,
        Guid? clientMessageId = null)
    {
        fixture.Llm.Enqueue(
            new LlmReply(null, [new LlmToolCall("call-1", tool, JsonSerializer.Serialize(arguments))]),
            new LlmReply("Bạn kiểm tra thẻ rồi bấm Xác nhận nhé.", []));
        return await SendAsync(client, sessionId, "Giúp mình với", clientMessageId);
    }

    private static async Task<ChatResponse> SendAsync(HttpClient client, Guid sessionId, string message, Guid? clientMessageId)
    {
        var response = await client.PostAsJsonAsync($"/api/chats/sessions/{sessionId}/messages", new { message, clientMessageId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<ChatResponse>(response);
    }

    private static async Task<ChatSessionDetailDto> GetSessionAsync(HttpClient client, Guid sessionId)
        => (await client.GetFromJsonAsync<ChatSessionDetailDto>($"/api/chats/sessions/{sessionId}", Json))!;

    private static async Task<ChatActionDto> GetCardAsync(HttpClient client, Guid sessionId)
        => (await GetSessionAsync(client, sessionId)).Turns[^1].Action!;

    private static async Task<AppointmentDto> GetAppointmentAsync(HttpClient client, int id)
        => (await client.GetFromJsonAsync<AppointmentDto>($"/api/appointments/{id}", Json))!;

    private Task<AppointmentDto> GetAppointmentByAdminAsync(int id) => GetAppointmentAsync(fixture.Client, id);

    private static async Task<WebDatLichKhamBenh.Application.DTOs.Patients.PatientDto> GetProfileAsync(HttpClient client)
        => (await client.GetFromJsonAsync<MyProfileDto>("/api/me/profile", Json))!.Patient!;

    private async Task<AppointmentDto> BookByAdminAsync(int patientId, int slotId)
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/appointments", new { patientId, appointmentSlotId = slotId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<AppointmentDto>(response);
    }

    private async Task<int> CountActiveAppointmentsAsync(int patientId, int slotId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Appointments.CountAsync(x => x.PatientId == patientId &&
            x.AppointmentSlotId == slotId && x.Status == AppointmentStatus.Confirmed);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json))!;
}
