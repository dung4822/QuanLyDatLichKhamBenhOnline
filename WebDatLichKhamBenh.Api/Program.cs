using WebDatLichKhamBenh.Application;
using WebDatLichKhamBenh.Infrastructure;
using FluentValidation;
using System.Text.Json.Serialization;
using WebDatLichKhamBenh.Api.Services;
using WebDatLichKhamBenh.Api.Middlewares;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using System.Threading.RateLimiting;
using Microsoft.OpenApi.Models;
using WebDatLichKhamBenh.Api.Security;
using WebDatLichKhamBenh.Application.Interfaces.Services;

var builder = WebApplication.CreateBuilder(args);

const string AllowFrontendDevPolicy = "AllowFrontendDev";

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer",
        Description = "Dán accessToken nhận từ POST /api/auth/login. Token là opaque bearer token, không phải JWT."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    // Endpoint mới mặc định chỉ Admin; public và các vai trò khác phải khai báo rõ.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole("Admin").Build();
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue<int?>("Authentication:RequestsPerMinute") ?? 20,
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
});
builder.Services.AddCors(options =>
{
    options.AddPolicy(AllowFrontendDevPolicy, policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "https://localhost:3000",
                "http://localhost:5173",
                "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<AppointmentSlotReconciliationService>();

var app = builder.Build();
if (args.Contains("--bootstrap-admin"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IAccountService>().BootstrapAdminAsync(
        builder.Configuration["BootstrapAdmin:Email"] ?? throw new InvalidOperationException("Thiếu BootstrapAdmin:Email."),
        builder.Configuration["BootstrapAdmin:Password"] ?? throw new InvalidOperationException("Thiếu BootstrapAdmin:Password."));
    Console.WriteLine("Đã khởi tạo Admin. Hãy đăng nhập và đổi mật khẩu.");
    return;
}
app.UseMiddleware<ApiExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(AllowFrontendDevPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();


app.Run();

public partial class Program { }
