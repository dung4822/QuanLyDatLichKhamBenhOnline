using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace WebDatLichKhamBenh.IntegrationTests;

// Mọi lỗi, dù sinh ra ở đâu (middleware, [ApiController], cookie, CSRF filter), đều phải cùng một hình dạng
// ProblemDetails: content-type application/problem+json, có status, title tiếng Việt và traceId.
public class ApiErrorFormatTests : IClassFixture<AppointmentApiFixture>
{
    private readonly AppointmentApiFixture _fixture;
    private HttpClient Client => _fixture.Client;
    public ApiErrorFormatTests(AppointmentApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Not_found_from_controller_is_problem_details()
        => await AssertProblemAsync(await Client.GetAsync("/api/doctors/999999"), 404, "Không tìm thấy dữ liệu.");

    [Fact]
    public async Task Fluent_validation_error_lists_failed_fields()
    {
        var response = await Client.PostAsJsonAsync("/api/patients", new { fullName = "AB", dateOfBirth = "2000-01-01", phoneNumber = "0901" });

        var problem = await AssertProblemAsync(response, 400, "Dữ liệu không hợp lệ.");
        Assert.True(problem.GetProperty("errors").TryGetProperty("FullName", out _));
    }

    [Fact]
    public async Task Malformed_json_is_problem_details()
    {
        using var content = new StringContent("{ bad json", Encoding.UTF8, "application/json");
        await AssertProblemAsync(await Client.PostAsync("/api/shifts", content), 400, "Dữ liệu không hợp lệ.");
    }

    [Fact]
    public async Task Missing_csrf_token_is_problem_details()
    {
        using var client = _fixture.CreateClient();
        var problem = await AssertProblemAsync(await client.PostAsJsonAsync("/api/auth/login", new { }), 400, "Dữ liệu không hợp lệ.");
        Assert.Contains("CSRF", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Unauthenticated_request_is_problem_details()
    {
        using var client = _fixture.CreateClient();
        await AssertProblemAsync(await client.GetAsync("/api/patients"), 401, "Vui lòng đăng nhập.");
    }

    [Fact]
    public async Task Forbidden_request_is_problem_details()
        => await AssertProblemAsync(await Client.PostAsJsonAsync("/api/chats", new { message = "Xin chào" }), 403, "Không có quyền truy cập.");

    [Fact]
    public async Task Business_conflict_is_problem_details()
    {
        var s = await _fixture.SeedAsync();
        var booked = await Client.PostAsJsonAsync("/api/appointments", new { patientId = s.PatientId, appointmentSlotId = s.Slots[0] });
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);

        var problem = await AssertProblemAsync(await Client.DeleteAsync($"/api/doctors/{s.DoctorId}"), 409, "Dữ liệu xung đột.");
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Unknown_route_is_problem_details()
        => await AssertProblemAsync(await Client.GetAsync("/api/khong-ton-tai"), 404, "Không tìm thấy dữ liệu.");

    [Fact]
    public async Task Wrong_http_method_is_problem_details()
        => await AssertProblemAsync(await Client.PatchAsync("/api/specialties", null), 405, "Phương thức HTTP không được hỗ trợ.");

    internal static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, int status, string title)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement.Clone();
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.StartsWith("https://tools.ietf.org/", problem.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        return problem;
    }
}
