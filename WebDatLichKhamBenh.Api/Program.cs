using WebDatLichKhamBenh.Application;
using WebDatLichKhamBenh.Infrastructure;
using System.Text.Json.Serialization;
using WebDatLichKhamBenh.Api.Services;
using WebDatLichKhamBenh.Api.Middlewares;
using WebDatLichKhamBenh.Api.Extensions;
using System.Threading.RateLimiting;
using Microsoft.OpenApi.Models;
using WebDatLichKhamBenh.Api.Security;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

const string AllowFrontendDevPolicy = "AllowFrontendDev";

builder.Services.AddControllers(options => options.Filters.Add<CsrfValidationFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("CSRF", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-CSRF-TOKEN",
        Description = "Gọi GET /api/auth/csrf và dán requestToken vào đây trước khi POST/PUT/PATCH/DELETE. " +
            "Sau đăng nhập/đăng xuất, lấy token mới. Cookie đăng nhập do trình duyệt tự gửi; dùng HTTPS."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "CSRF" } }] = Array.Empty<string>()
    });
});
builder.Services.AddClinicProblemDetails();
builder.Services.AddClinicAuthentication();
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = (context, _) => new ValueTask(context.HttpContext.WriteProblemAsync(
        StatusCodes.Status429TooManyRequests, "Bạn gửi yêu cầu quá nhanh. Vui lòng thử lại sau ít phút."));
    options.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
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
            .AllowAnyMethod()
            .AllowCredentials();
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
// Response lỗi KHÔNG có body (route không tồn tại, sai HTTP method...) cũng được ghi thành ProblemDetails.
app.UseStatusCodePages();

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
