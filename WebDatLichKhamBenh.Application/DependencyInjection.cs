using Microsoft.Extensions.DependencyInjection;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Services;

namespace WebDatLichKhamBenh.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ISpecialtyService, SpecialtyService>();

        return services;
    }
}
