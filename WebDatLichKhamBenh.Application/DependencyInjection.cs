using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Services;
using FluentValidation;

namespace WebDatLichKhamBenh.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services.AddScoped<ISpecialtyService, SpecialtyService>();
        services.AddScoped<IDoctorService, DoctorService>();
        return services;
    }
}
