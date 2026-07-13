using HemodinksAPI.Application.Tenancy;

namespace HemodinksAPI.Api;

public static partial class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddTenancy(this IServiceCollection services)
    {
        services.AddScoped<ClinicaContext>();
        services.AddScoped<IClinicaContext>(provider => provider.GetRequiredService<ClinicaContext>());
        services.AddScoped<ClinicaResolutionService>();

        return services;
    }
}
