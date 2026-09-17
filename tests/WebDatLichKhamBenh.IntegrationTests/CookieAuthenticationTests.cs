using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebDatLichKhamBenh.Application.DTOs.Accounts;

namespace WebDatLichKhamBenh.IntegrationTests;

public class CookieAuthenticationTests(AppointmentApiFixture fixture) : IClassFixture<AppointmentApiFixture>
{
    private const string CookieName = "__Host-Clinic.Auth";
    private const string CsrfHeader = "X-CSRF-TOKEN";
    private const string Password = "Cookie-test-password-2026!";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    private async Task<AccountDto> CreateAccountAsync()
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/accounts", new
        {
            email = Guid.NewGuid().ToString("N") + "@example.test",
            password = Password,
            displayName = "Kiểm thử cookie",
            role = "Receptionist"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountDto>(Json))!;
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, AccountDto account)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { account.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private static string GetAuthCookie(HttpResponseMessage response)
    {
        return response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal)).Split(';')[0];
    }

    [Fact]
    public async Task Login_sets_secure_cookie_returns_no_token_and_does_not_redirect_on_forbidden()
    {
        var account = await CreateAccountAsync();
        using var client = await fixture.CreateClientWithCsrfAsync();
        using var response = await LoginAsync(client, account);

        var setCookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(CookieName + "="));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.CacheControl?.NoStore);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("accessToken", out _));
        Assert.False(body.TryGetProperty("tokenType", out _));
        Assert.False(body.TryGetProperty("sessionId", out _));
        var expiresAt = body.GetProperty("expiresAt").GetDateTime();
        Assert.InRange(expiresAt, DateTime.UtcNow.AddHours(7.9), DateTime.UtcNow.AddHours(8.1));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/profile")).StatusCode);

        using var forbidden = await client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Null(forbidden.Headers.Location);
        Assert.Equal("application/problem+json", forbidden.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/register")]
    public async Task Anonymous_auth_posts_require_csrf(string path)
    {
        using var client = fixture.CreateClient();
        using var response = await client.PostAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CSRF", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("PUT", "/api/accounts/00000000-0000-0000-0000-000000000001/access")]
    [InlineData("PATCH", "/api/appointments/1/cancel")]
    [InlineData("DELETE", "/api/specialties/1")]
    public async Task Every_mutation_verb_requires_csrf_even_with_valid_cookie(string method, string path)
    {
        using var client = await fixture.CreateClientWithCsrfAsync();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@example.test", password = "Test-admin-password-2026!"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Remove(CsrfHeader);

        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CSRF", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Csrf_token_must_match_browser_cookie_and_current_identity()
    {
        var account = await CreateAccountAsync();
        using var client = await fixture.CreateClientWithCsrfAsync();
        using var stranger = await fixture.CreateClientWithCsrfAsync();
        var ownToken = client.DefaultRequestHeaders.GetValues(CsrfHeader).Single();
        var foreignToken = stranger.DefaultRequestHeaders.GetValues(CsrfHeader).Single();

        client.DefaultRequestHeaders.Remove(CsrfHeader);
        client.DefaultRequestHeaders.Add(CsrfHeader, foreignToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login",
            new { account.Email, password = Password })).StatusCode);

        client.DefaultRequestHeaders.Remove(CsrfHeader);
        client.DefaultRequestHeaders.Add(CsrfHeader, ownToken);
        using var login = await LoginAsync(client, account);
        // Token anonymous cũ không hợp lệ sau khi đã đăng nhập.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        await AppointmentApiFixture.RefreshCsrfTokenAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login",
            new { account.Email, password = Password })).StatusCode);
        await AppointmentApiFixture.RefreshCsrfTokenAsync(client);
        using var nextLogin = await LoginAsync(client, account);
    }

    [Fact]
    public async Task Logout_revokes_copied_cookie_but_preserves_another_session()
    {
        var account = await CreateAccountAsync();
        using var client = await fixture.CreateClientWithCsrfAsync();
        using var other = await fixture.CreateClientWithCsrfAsync();
        using var login = await LoginAsync(client, account);
        using var otherLogin = await LoginAsync(other, account);
        using var replay = fixture.CreateClient(handleCookies: false);
        replay.DefaultRequestHeaders.Add("Cookie", GetAuthCookie(login));

        await AppointmentApiFixture.RefreshCsrfTokenAsync(client);
        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), x => x.StartsWith(CookieName + "=;") && x.Contains("expires="));
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/me/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Tampered_and_expired_tickets_are_rejected_even_when_database_session_exists()
    {
        var account = await CreateAccountAsync();
        using var client = await fixture.CreateClientWithCsrfAsync();
        using var login = await LoginAsync(client, account);
        var cookie = GetAuthCookie(login);
        var value = cookie[(CookieName.Length + 1)..];
        using var replay = fixture.CreateClient(handleCookies: false);
        replay.DefaultRequestHeaders.Add("Cookie", CookieName + "=invalid" + value);
        using var tampered = await replay.GetAsync("/api/me/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
        Assert.Null(tampered.Headers.Location);

        var options = fixture.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = options.TicketDataFormat.Unprotect(value)!;
        ticket.Properties.IssuedUtc = DateTimeOffset.UtcNow.AddHours(-9);
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(-1);
        replay.DefaultRequestHeaders.Remove("Cookie");
        replay.DefaultRequestHeaders.Add("Cookie", CookieName + "=" + options.TicketDataFormat.Protect(ticket));
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/me/profile")).StatusCode);

        await using var db = fixture.CreateDbContext();
        Assert.True(await db.UserSessions.AnyAsync(x => x.UserAccountId == account.UserAccountId && x.ExpiresAt > DateTime.UtcNow));
    }

    [Fact]
    public async Task Cors_allows_credentials_only_for_configured_frontend_origins()
    {
        using var client = fixture.CreateClient();
        foreach (var origin in new[] { "https://localhost:5173", "https://untrusted.example" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");
            using var response = await client.SendAsync(request);
            if (origin == "https://localhost:5173")
            {
                Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
                Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
            }
            else
            {
                Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
            }
        }
    }
}
