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
        services.AddScoped<PlatformDbContext>();

        services.AddScoped<IUserFeatureDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IUserDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IPatientFeatureDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ICbhpmFeatureDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IMedicalGroupFeatureDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IEventFeatureDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IFinanceFeatureDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IFaturamentoMedicoFeatureDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IDashboardFeatureDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ILicensingFeatureDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ICatalogQueryDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IClinicalReferenceDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IClinicDirectoryDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IGlobalIdentityDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ITeamDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IUserSearchDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IProfileDirectoryDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IPasswordCredentialDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IPasswordResetOperationsDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IPlatformPasswordResetDbContext>(provider => provider.GetRequiredService<PlatformDbContext>());
        services.AddScoped<IPlatformTeamDbContext>(provider => provider.GetRequiredService<PlatformDbContext>());
        services.AddScoped<IPlatformClinicDbContext>(provider => provider.GetRequiredService<PlatformDbContext>());
        services.AddScoped<ISessionDbContext>(provider => provider.GetRequiredService<PlatformDbContext>());
        services.AddScoped<ILegalAcceptanceDbContext>(provider => provider.GetRequiredService<AppDbContext>());
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
