using HemodinksAPI.Application.Storage;
using HemodinksAPI.Infrastructure.Storage;
using HemodinksAPI.Infrastructure.Utils;

namespace HemodinksAPI.Api;

public static partial class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.Configure<ProfilePhotoStorageOptions>(configuration.GetSection("AzureStorage"));
        services.Configure<PatientFileStorageOptions>(options =>
        {
            var azureStorage = configuration.GetSection("AzureStorage");
            options.ConnectionString = azureStorage["ConnectionString"];
            options.ContainerName = azureStorage["PatientFilesContainerName"] ?? "patient-files";
            options.PublicBaseUrl = azureStorage["PatientFilesPublicBaseUrl"];

            if (long.TryParse(azureStorage["PatientFileMaxBytes"], out var maxBytes))
            {
                options.MaxBytes = maxBytes;
            }
        });

        services.Configure<LocalStorageOptions>(options =>
        {
            configuration.GetSection("LocalStorage").Bind(options);
            options.RootPath = LocalStoragePathHelper.ResolveRootPath(options.RootPath, environment.ContentRootPath);
            options.RequestPath = LocalStoragePathHelper.NormalizeRequestPath(options.RequestPath);
            options.PublicBaseUrl = LocalStoragePathHelper.NormalizePublicBaseUrl(options.PublicBaseUrl);
        });

        var azureConnectionString = configuration["AzureStorage:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(azureConnectionString))
        {
            services.AddSingleton<IProfilePhotoStorage, AzureBlobProfilePhotoStorage>();
            services.AddSingleton<IPatientFileStorage, AzureBlobPatientFileStorage>();
            return services;
        }

        if (environment.IsProduction())
        {
            throw new InvalidOperationException("AzureStorage:ConnectionString must be configured in production.");
        }

        services.AddSingleton<IProfilePhotoStorage, LocalDiskProfilePhotoStorage>();
        services.AddSingleton<IPatientFileStorage, LocalDiskPatientFileStorage>();
        return services;
    }
}
