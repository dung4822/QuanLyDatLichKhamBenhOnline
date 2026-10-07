using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Infrastructure.Persistence;
using WebDatLichKhamBenh.Infrastructure.Repositories;
using WebDatLichKhamBenh.Infrastructure.Storage;
using WebDatLichKhamBenh.Infrastructure.AI;

namespace WebDatLichKhamBenh.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<ISpecialtyRepository, SpecialtyRepository>();
        services.AddScoped<IDoctorRepository, DoctorRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IDoctorShiftRepository, DoctorShiftRepository>();
        services.AddScoped<INonWorkingDayRepository, NonWorkingDayRepository>();
        services.AddScoped<IAppointmentSlotRepository, AppointmentSlotRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddSingleton<IPasswordService, WebDatLichKhamBenh.Infrastructure.Security.PasswordService>();
        services.AddScoped<IClinicScheduleTransaction, ClinicScheduleTransaction>();
        services.AddSingleton(new CloudinarySettings
        {
            CloudName = configuration[$"{CloudinarySettings.SectionName}:CloudName"] ?? string.Empty,
            ApiKey = configuration[$"{CloudinarySettings.SectionName}:ApiKey"] ?? string.Empty,
            ApiSecret = configuration[$"{CloudinarySettings.SectionName}:ApiSecret"] ?? string.Empty
        });
        services.AddSingleton<IImageStorageService, CloudinaryImageStorageService>();
        services.AddSingleton(new DeepSeekSettings
        {
            ApiKey = configuration["DeepSeek:ApiKey"] ?? string.Empty,
            Model = configuration["DeepSeek:Model"] ?? "deepseek-flash",
            MaxOutputTokens = configuration.GetValue<int?>("DeepSeek:MaxOutputTokens") ?? 1024
        });
        // Thời gian chờ tổng (60 giây) do ChatService quản lý.
        services.AddHttpClient<ILlmClient, DeepSeekClient>();

        return services;
    }
}
