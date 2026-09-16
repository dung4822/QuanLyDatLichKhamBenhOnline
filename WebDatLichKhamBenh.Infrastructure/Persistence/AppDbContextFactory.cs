using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
namespace WebDatLichKhamBenh.Infrastructure.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "WebDatLichKhamBenh.Api")))
            root = root.Parent;
        var apiPath = root is null ? Directory.GetCurrentDirectory() : Path.Combine(root.FullName, "WebDatLichKhamBenh.Api");
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var configuration = new ConfigurationBuilder().SetBasePath(apiPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true);
        if (environment == "Development") configuration.AddUserSecrets("web-dat-lich-kham-benh-api");
        configuration.AddEnvironmentVariables();
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(configuration.Build().GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Thiếu ConnectionStrings:DefaultConnection."));

        return new AppDbContext(optionsBuilder.Options);
    }
}
