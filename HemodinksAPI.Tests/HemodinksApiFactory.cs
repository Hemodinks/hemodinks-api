using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.HostedServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HemodinksAPI.Tests;

internal sealed class HemodinksApiFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? _configureServices;

    public HemodinksApiFactory(Action<IServiceCollection>? configureServices = null)
    {
        _configureServices = configureServices;
        ConfigureEnvironment();
    }

    private static void ConfigureEnvironment()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Server=(localdb)\\MSSQLLocalDB;Database=HemodinksApiTests;Trusted_Connection=True;TrustServerCertificate=True");
        Environment.SetEnvironmentVariable("JwtSettings__SecretKey", "0123456789abcdef0123456789abcdef");
        Environment.SetEnvironmentVariable("JwtSettings__Issuer", "HemodinksAPI");
        Environment.SetEnvironmentVariable("JwtSettings__Audience", "HemodinksAPI");
        Environment.SetEnvironmentVariable("JwtSettings__ExpirationMinutes", "60");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\MSSQLLocalDB;Database=HemodinksApiTests;Trusted_Connection=True;TrustServerCertificate=True",
                ["JwtSettings:SecretKey"] = "0123456789abcdef0123456789abcdef",
                ["JwtSettings:Issuer"] = "HemodinksAPI",
                ["JwtSettings:Audience"] = "HemodinksAPI",
                ["JwtSettings:ExpirationMinutes"] = "60",
                ["ApiDocumentation:Enabled"] = "true",
                ["Database:RunMigrationsOnStartup"] = "true",
                ["Seed:CbhpmOnStartup"] = "true",
                ["Seed:UsersOnStartup"] = "true",
                ["PasswordReset:UseEmail"] = "true",
                ["PasswordReset:ExposeTokenInResponse"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            var databaseName = $"HemodinksApiTests-{Guid.NewGuid():N}";
            var dbContextDescriptors = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(AppDbContext)
                    || descriptor.ServiceType == typeof(DbContextOptions)
                    || descriptor.ServiceType == typeof(DbContextOptions<AppDbContext>)
                    || descriptor.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration") == true)
                .ToList();

            foreach (var descriptor in dbContextDescriptors)
            {
                services.Remove(descriptor);
            }

            var eventWorker = services.FirstOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(EventNotificationHostedService));

            if (eventWorker != null)
            {
                services.Remove(eventWorker);
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));

            _configureServices?.Invoke(services);
        });
    }
}
