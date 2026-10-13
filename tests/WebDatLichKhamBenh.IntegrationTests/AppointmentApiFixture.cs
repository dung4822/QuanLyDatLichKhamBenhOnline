using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Net.Http.Json;
using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Api.Services;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.IntegrationTests;

public sealed class AppointmentApiFixture : IAsyncLifetime
{
    private readonly string _databaseName = "appointment_tests_" + Guid.NewGuid().ToString("N");
    private readonly string _adminConnection = Environment.GetEnvironmentVariable("APPOINTMENT_TEST_POSTGRES")
        ?? "Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Pooling=false";
    private WebApplicationFactory<Program> _factory = null!;
    public HttpClient Client { get; private set; } = null!;
    public string ConnectionString { get; private set; } = string.Empty;
    public IServiceProvider Services => _factory.Services;
    public SaveFailureInterceptor Failures { get; } = new();
    public QueuedLlm Llm { get; } = new();

    public async Task InitializeAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _databaseName };
        ConnectionString = builder.ConnectionString;
        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        await using (var db = CreateDbContext())
            await db.Database.MigrateAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:RequestsPerMinute"] = "1000" }));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options
                    .UseNpgsql(ConnectionString).AddInterceptors(Failures));
                var worker = services.Single(x => x.ServiceType == typeof(IHostedService) &&
                    x.ImplementationType == typeof(AppointmentSlotReconciliationService));
                services.Remove(worker);
                // Không gọi DeepSeek thật trong test; test chat tự xếp câu trả lời của AI vào hàng đợi.
                services.RemoveAll<ILlmClient>();
                services.AddSingleton<ILlmClient>(Llm);
            });
        });
        Client = await CreateClientWithCsrfAsync();
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAccountService>().BootstrapAdminAsync("admin@example.test", "Test-admin-password-2026!");
        var login = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = "admin@example.test", Password = "Test-admin-password-2026!" });
        login.EnsureSuccessStatusCode();
        await RefreshCsrfTokenAsync(Client);
    }

    public HttpClient CreateClient(bool handleCookies = true) => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = handleCookies
    });

    public async Task<HttpClient> CreateClientWithCsrfAsync()
    {
        var client = CreateClient();
        await RefreshCsrfTokenAsync(client);
        return client;
    }

    public static async Task RefreshCsrfTokenAsync(HttpClient client)
    {
        var tokens = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", tokens!.RequestToken);
    }

    private record CsrfTokenResponse(string RequestToken);

    public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString).Options);

    public async Task<Scenario> SeedAsync()
    {
        await using var db = CreateDbContext();
        var tag = Guid.NewGuid().ToString("N")[..10];
        var specialty = new Specialty { Name = "Khoa " + tag, Status = "Active", CreatedAt = DateTime.UtcNow };
        var doctor = new Doctor { FullName = "Bác sĩ " + tag, Email = tag + "@example.test",
            Specialty = specialty, CareerStartDate = new DateOnly(2020, 1, 1), CreatedAt = DateTime.UtcNow };
        var otherDoctor = new Doctor { FullName = "Bác sĩ khác " + tag, Email = "other" + tag + "@example.test",
            Specialty = specialty, CareerStartDate = new DateOnly(2020, 1, 1), CreatedAt = DateTime.UtcNow };
        var patient = new Patient { FullName = "Bệnh nhân " + tag, PhoneNumber = "0901234567",
            DateOfBirth = new DateOnly(2000, 1, 1), CreatedAt = DateTime.UtcNow };
        var otherPatient = new Patient { FullName = "Bệnh nhân khác " + tag, PhoneNumber = "0907654321",
            DateOfBirth = new DateOnly(2001, 1, 1), CreatedAt = DateTime.UtcNow };
        var date = ClinicClock.Today.AddDays(1);
        var shift = new Shift { Name = "Ca sáng", DayOfWeek = date.DayOfWeek,
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) };
        var assignment = new DoctorShift { Doctor = doctor, Shift = shift, CreatedAt = DateTime.UtcNow };
        var otherAssignment = new DoctorShift { Doctor = otherDoctor, Shift = shift, CreatedAt = DateTime.UtcNow };
        db.AddRange(assignment, otherAssignment, patient, otherPatient);
        await db.SaveChangesAsync();
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAppointmentSlotService>().EnsureRollingWindowAsync();
        var slots = await db.AppointmentSlots.Where(x => x.DoctorId == doctor.DoctorId && x.Date == date)
            .OrderBy(x => x.StartTime).Select(x => x.AppointmentSlotId).ToListAsync();
        var otherSlot = await db.AppointmentSlots.Where(x => x.DoctorId == otherDoctor.DoctorId && x.Date == date)
            .OrderBy(x => x.StartTime).Select(x => x.AppointmentSlotId).FirstAsync();
        return new Scenario(patient.PatientId, otherPatient.PatientId, doctor.DoctorId, otherDoctor.DoctorId,
            specialty.SpecialtyId, shift.ShiftId, assignment.DoctorShiftId, slots, otherSlot, date);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        // Tên được tạo nội bộ, chỉ xóa database của lần test này.
        if (!_databaseName.StartsWith("appointment_tests_", StringComparison.Ordinal)) return;
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

public record Scenario(int PatientId, int OtherPatientId, int DoctorId, int OtherDoctorId,
    int SpecialtyId, int ShiftId, int AssignmentId, List<int> Slots, int OtherSlot, DateOnly Date);

public sealed class QueuedLlm : ILlmClient
{
    private readonly Queue<LlmReply> _replies = new();
    public int CallCount { get; private set; }

    public void Enqueue(params LlmReply[] replies)
    {
        foreach (var reply in replies) _replies.Enqueue(reply);
    }

    public Task<LlmReply> CompleteAsync(IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : new LlmReply("Xin chào", []));
    }
}

public sealed class SaveFailureInterceptor : SaveChangesInterceptor
{
    public bool FailNextSlotSave { get; set; }
    public bool FailNextAppointmentUpdate { get; set; }
    public bool FailNextHistorySave { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context!;
        if (FailNextHistorySave && context.ChangeTracker.Entries<AppointmentHistory>().Any(x => x.State == EntityState.Added))
        {
            FailNextHistorySave = false;
            throw new InvalidOperationException("Simulated audit persistence failure.");
        }
        if (FailNextAppointmentUpdate && context.ChangeTracker.Entries<Appointment>()
            .Any(x => x.State == EntityState.Modified))
        {
            FailNextAppointmentUpdate = false;
            throw new InvalidOperationException("Simulated appointment update failure.");
        }
        if (context.ChangeTracker.Entries<Appointment>().Any(x =>
            x.State == EntityState.Added && x.Entity.Reason == "SIMULATE_SAVE_FAILURE"))
            throw new InvalidOperationException("Simulated appointment persistence failure.");
        if (FailNextSlotSave && context.ChangeTracker.Entries<AppointmentSlot>()
            .Any(x => x.State == EntityState.Modified))
        {
            FailNextSlotSave = false;
            throw new InvalidOperationException("Simulated slot persistence failure.");
        }
        return ValueTask.FromResult(result);
    }
}
