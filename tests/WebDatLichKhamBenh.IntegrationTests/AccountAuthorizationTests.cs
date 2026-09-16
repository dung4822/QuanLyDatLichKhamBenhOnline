using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.IntegrationTests;

public class AccountAuthorizationTests(AppointmentApiFixture fixture) : IClassFixture<AppointmentApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private const string Password = "Patient-test-password-2026!";

    private async Task<(HttpClient Client, AccountDto Account)> CreateLinkedAsync(AccountRole role, int? patientId = null, int? doctorId = null)
    {
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var response = await fixture.Client.PostAsJsonAsync("/api/accounts", new
        { email, password = Password, displayName = "Tài khoản thử nghiệm", role = role.ToString(), patientId, doctorId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = (await response.Content.ReadFromJsonAsync<AccountDto>(Json))!;
        var client = await LoginAsync(email);
        return (client, account);
    }

    private async Task<HttpClient> LoginAsync(string email, string password = Password)
    {
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private async Task<AppointmentDto> BookAsync(int patientId, int slotId)
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/appointments", new { patientId, appointmentSlotId = slotId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AppointmentDto>(Json))!;
    }

    [Fact]
    public async Task Anonymous_can_browse_public_catalog_but_cannot_read_private_or_write()
    {
        using var client = fixture.CreateClient();
        foreach (var path in new[] { "/api/doctors", "/api/specialties" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/api/patients", "/api/appointments", "/api/me/profile", "/api/accounts" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/specialties", new { name = "No auth", status = "Active" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", new string('A', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Registration_creates_new_patient_and_cannot_choose_role_or_claim_existing_profile()
    {
        var s = await fixture.SeedAsync();
        using var anonymous = fixture.CreateClient();
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var response = await anonymous.PostAsJsonAsync("/api/auth/register", new
        {
            email, password = Password, role = "Admin", patientId = s.PatientId, doctorId = s.DoctorId,
            profile = new { fullName = "Bệnh nhân đăng ký", dateOfBirth = "2000-01-01", gender = 0, phoneNumber = "0901234567" }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = (await response.Content.ReadFromJsonAsync<AccountDto>(Json))!;
        Assert.Equal(AccountRole.Patient, account.Role);
        Assert.NotEqual(s.PatientId, account.PatientId);
        Assert.Null(account.DoctorId);
        using var client = await LoginAsync(email.ToUpperInvariant());
        var me = (await client.GetFromJsonAsync<MyProfileDto>("/api/me/profile", Json))!;
        Assert.Equal(account.PatientId, me.Patient!.PatientId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/accounts")).StatusCode);
        await using var db = fixture.CreateDbContext();
        var persisted = await db.UserAccounts.FindAsync(account.UserAccountId);
        Assert.NotEqual(Password, persisted!.PasswordHash);
        Assert.DoesNotContain("passwordHash", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        Assert.DoesNotContain(await db.UserSessions.ToListAsync(), x => x.TokenHash == token);
        var duplicate = await anonymous.PostAsJsonAsync("/api/auth/register", new
        {
            email = email.ToUpperInvariant(), password = Password,
            profile = new { fullName = "Bệnh nhân trùng", dateOfBirth = "2000-01-01", phoneNumber = "0901234567" }
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Patient_is_scoped_on_both_old_and_me_endpoints_and_cannot_mutate_other_records()
    {
        var s = await fixture.SeedAsync();
        var (client, _) = await CreateLinkedAsync(AccountRole.Patient, s.PatientId);
        using var ownedClient = client;
        var own = await BookAsync(s.PatientId, s.Slots[0]);
        var other = await BookAsync(s.OtherPatientId, s.OtherSlot);
        foreach (var path in new[] { "/api/appointments", "/api/me/appointments" })
        {
            var page = (await client.GetFromJsonAsync<AppointmentPageDto>(path, Json))!;
            Assert.Single(page.Items);
            Assert.Equal(own.AppointmentId, page.Items[0].AppointmentId);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path + "?patientId=" + s.OtherPatientId)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/appointments/{other.AppointmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/appointments/{other.AppointmentId}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync($"/api/appointments/{other.AppointmentId}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync($"/api/appointments/{other.AppointmentId}/reschedule", new { appointmentSlotId = s.Slots[1] })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/appointments", new { patientId = s.OtherPatientId, appointmentSlotId = s.Slots[1] })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsync($"/api/appointments/{own.AppointmentId}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/patients")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{s.OtherPatientId}")).StatusCode);
        var update = new { fullName = "Tên mới của tôi", dateOfBirth = "2000-01-01", phoneNumber = "0901111222", gender = 0 };
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/patients/{s.OtherPatientId}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/me/profile", update)).StatusCode);
        var me = (await client.GetFromJsonAsync<MyProfileDto>("/api/me/profile", Json))!;
        Assert.Equal("Tên mới của tôi", me.Patient!.FullName);
    }

    [Fact]
    public async Task Doctor_only_reads_assigned_appointments_and_records_their_own_completion()
    {
        var s = await fixture.SeedAsync();
        var (client, account) = await CreateLinkedAsync(AccountRole.Doctor, doctorId: s.DoctorId);
        using var doctorClient = client;
        var own = await BookAsync(s.PatientId, s.Slots[0]);
        var other = await BookAsync(s.OtherPatientId, s.OtherSlot);
        var page = (await client.GetFromJsonAsync<AppointmentPageDto>("/api/me/appointments", Json))!;
        Assert.Single(page.Items);
        Assert.Equal(own.AppointmentId, page.Items[0].AppointmentId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/appointments?doctorId=" + s.OtherDoctorId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/appointments/{other.AppointmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsync($"/api/appointments/{other.AppointmentId}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/appointments/{own.AppointmentId}/cancel", new { })).StatusCode);
        var me = (await client.GetFromJsonAsync<MyProfileDto>("/api/me/profile", Json))!;
        Assert.Equal(s.DoctorId, me.Doctor!.DoctorId);
        await using (var db = fixture.CreateDbContext())
        {
            var appointment = (await db.Appointments.FindAsync(own.AppointmentId))!;
            appointment.Date = s.Date.AddDays(-2);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsync($"/api/appointments/{own.AppointmentId}/complete", null)).StatusCode);
        var history = (await client.GetFromJsonAsync<List<AppointmentHistoryDto>>($"/api/appointments/{own.AppointmentId}/history", Json))!;
        Assert.Equal("Completed", history[0].Action);
        Assert.Equal(account.UserAccountId, history[0].ActorAccountId);
        Assert.Equal("Doctor", history[0].ActorRole);
    }

    [Fact]
    public async Task Receptionist_manages_appointments_but_cannot_create_accounts_or_change_catalog()
    {
        var s = await fixture.SeedAsync();
        var (client, _) = await CreateLinkedAsync(AccountRole.Receptionist);
        using var staffClient = client;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/patients")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/appointments", new { patientId = s.PatientId, appointmentSlotId = s.Slots[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/doctors/{s.DoctorId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/specialties", new { name = "Unauthorized", status = "Active" })).StatusCode);
    }

    [Fact]
    public async Task History_keeps_before_after_actor_and_does_not_duplicate_on_retry()
    {
        var s = await fixture.SeedAsync();
        var (client, account) = await CreateLinkedAsync(AccountRole.Patient, s.PatientId);
        using var patientClient = client;
        var created = await client.PostAsJsonAsync("/api/appointments", new { patientId = s.PatientId, appointmentSlotId = s.Slots[0] });
        var appointment = (await created.Content.ReadFromJsonAsync<AppointmentDto>(Json))!;
        var path = $"/api/appointments/{appointment.AppointmentId}";
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync(path + "/reschedule", new { appointmentSlotId = s.OtherSlot })).StatusCode);
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync(path + "/cancel", new { reason = "Bận việc" })).StatusCode);
        var history = (await client.GetFromJsonAsync<List<AppointmentHistoryDto>>(path + "/history", Json))!;
        Assert.Equal(3, history.Count);
        Assert.All(history, x => Assert.Equal(account.UserAccountId, x.ActorAccountId));
        Assert.Equal(new[] { "Cancelled", "Rescheduled", "Created" }, history.Select(x => x.Action));
        Assert.Equal(s.DoctorId, history[1].Before!.DoctorId);
        Assert.Equal(s.OtherDoctorId, history[1].After.DoctorId);
        Assert.Equal(s.Slots[0], history[1].Before!.AppointmentSlotId);
        Assert.Equal(s.OtherSlot, history[1].After.AppointmentSlotId);
        Assert.Null(history[2].Before);
        Assert.Equal("Bận việc", history[0].After.CancellationReason);
        Assert.Single((await client.GetFromJsonAsync<List<AppointmentHistoryDto>>(path + "/history?pageSize=1", Json))!);
        // Hồ sơ thay đổi không sửa bản chụp lịch sử.
        await using var db = fixture.CreateDbContext();
        (await db.UserAccounts.FindAsync(account.UserAccountId))!.DisplayName = "Tên tài khoản mới";
        await db.SaveChangesAsync();
        history = (await client.GetFromJsonAsync<List<AppointmentHistoryDto>>(path + "/history", Json))!;
        Assert.All(history, x => Assert.Equal("Tài khoản thử nghiệm", x.ActorDisplayName));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_booking_and_reschedule_with_the_slots()
    {
        var s = await fixture.SeedAsync();
        fixture.Failures.FailNextHistorySave = true;
        var failed = await fixture.Client.PostAsJsonAsync("/api/appointments", new { patientId = s.PatientId, appointmentSlotId = s.Slots[0] });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.False(await db.Appointments.AnyAsync(x => x.PatientId == s.PatientId));
            Assert.Equal(AppointmentSlotStatus.Available, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
        }
        var appointment = await BookAsync(s.PatientId, s.Slots[0]);
        fixture.Failures.FailNextHistorySave = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await fixture.Client.PatchAsJsonAsync(
            $"/api/appointments/{appointment.AppointmentId}/reschedule", new { appointmentSlotId = s.Slots[1] })).StatusCode);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(s.Slots[0], (await db.Appointments.FindAsync(appointment.AppointmentId))!.AppointmentSlotId);
            Assert.Equal(AppointmentSlotStatus.Booked, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
            Assert.Equal(AppointmentSlotStatus.Available, (await db.AppointmentSlots.FindAsync(s.Slots[1]))!.Status);
            Assert.Equal(1, await db.AppointmentHistories.CountAsync(x => x.AppointmentId == appointment.AppointmentId));
        }
    }

    [Fact]
    public async Task Logout_password_change_expiry_and_disabling_account_revoke_access()
    {
        var s = await fixture.SeedAsync();
        var (client, account) = await CreateLinkedAsync(AccountRole.Patient, s.PatientId);
        using var original = client;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me/profile")).StatusCode);
        using var next = await LoginAsync(account.Email);
        using var otherSession = await LoginAsync(account.Email);
        var newPassword = "New-password-2026!";
        Assert.Equal(HttpStatusCode.NoContent, (await next.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = Password, newPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await next.GetAsync("/api/me/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherSession.GetAsync("/api/me/profile")).StatusCode);
        using var renewed = await LoginAsync(account.Email, newPassword);
        await using (var db = fixture.CreateDbContext())
            await db.UserSessions.Where(x => x.UserAccountId == account.UserAccountId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await renewed.GetAsync("/api/me/profile")).StatusCode);
        using var disabled = await LoginAsync(account.Email, newPassword);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PutAsJsonAsync($"/api/accounts/{account.UserAccountId}/access",
            new { role = "Patient", patientId = s.PatientId, isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await disabled.GetAsync("/api/me/profile")).StatusCode);
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = newPassword })).StatusCode);
    }

    [Fact]
    public async Task Link_changes_revoke_old_sessions_and_links_are_unique_and_role_checked()
    {
        var s = await fixture.SeedAsync();
        var (client, account) = await CreateLinkedAsync(AccountRole.Patient, s.PatientId);
        using var oldClient = client;
        var response = await fixture.Client.PostAsJsonAsync("/api/accounts", new
        { email = "duplicate-" + account.Email, password = Password, displayName = "Duplicate", role = "Patient", patientId = s.PatientId });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PutAsJsonAsync($"/api/accounts/{account.UserAccountId}/access",
            new { role = "Doctor", patientId = s.PatientId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PutAsJsonAsync($"/api/accounts/{account.UserAccountId}/access",
            new { role = "Patient", patientId = s.OtherPatientId })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me/profile")).StatusCode);
        using var relinked = await LoginAsync(account.Email);
        Assert.Equal(s.OtherPatientId, (await relinked.GetFromJsonAsync<MyProfileDto>("/api/me/profile", Json))!.Patient!.PatientId);
    }

    [Fact]
    public async Task Last_admin_is_protected_and_failed_logins_lock_account()
    {
        var admin = (await fixture.Client.GetFromJsonAsync<MyProfileDto>("/api/me/profile", Json))!.Account;
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.PutAsJsonAsync($"/api/accounts/{admin.UserAccountId}/access",
            new { role = "Admin", isActive = false })).StatusCode);
        var (client, account) = await CreateLinkedAsync(AccountRole.Receptionist);
        using var staffClient = client;
        using var anonymous = fixture.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = Password })).StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.True((await db.UserAccounts.FindAsync(account.UserAccountId))!.LockedUntil > DateTime.UtcNow);
    }
}
