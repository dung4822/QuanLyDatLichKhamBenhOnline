using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Infrastructure.Persistence;
using WebDatLichKhamBenh.Infrastructure.Repositories;
using WebDatLichKhamBenh.Infrastructure.Storage;

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
        services.AddSingleton(new CloudinarySettings
        {
            CloudName = configuration[$"{CloudinarySettings.SectionName}:CloudName"] ?? string.Empty,
            ApiKey = configuration[$"{CloudinarySettings.SectionName}:ApiKey"] ?? string.Empty,
            ApiSecret = configuration[$"{CloudinarySettings.SectionName}:ApiSecret"] ?? string.Empty
        });
        services.AddSingleton<IImageStorageService, CloudinaryImageStorageService>();

        return services;
    }
}
