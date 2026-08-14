using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HemodinksAPI.Api;

public static partial class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(defaultConnection))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(defaultConnection, sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
                sqlOptions.EnableRetryOnFailure();
            }));

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IClinicDirectoryDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IGlobalIdentityDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ITeamDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IPlatformTeamDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IPlatformClinicDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ISessionDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IFinanceEndpointDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<EfDataExecution>();
        services.AddScoped<IDataExecutionStrategy>(provider => provider.GetRequiredService<EfDataExecution>());
        services.AddScoped<IDataTransactionManager>(provider => provider.GetRequiredService<EfDataExecution>());
        services.AddScoped<IFullTextSearchCapability, SqlServerFullTextSearchCapability>();
        services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(
                "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);

        return services;
    }
}
