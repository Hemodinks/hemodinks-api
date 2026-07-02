using HemodinksAPI.Application.Storage;
using HemodinksAPI.Infrastructure.Services;
using HemodinksAPI.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.Configure<EmailOptions>(context.Configuration.GetSection("Email"));
        services.Configure<FrontendOptions>(context.Configuration.GetSection("Frontend"));
        services.Configure<ProfilePhotoStorageOptions>(options =>
        {
            var azureStorage = context.Configuration.GetSection("AzureStorage");
            options.ConnectionString = azureStorage["ConnectionString"]
                ?? context.Configuration["AzureWebJobsStorage"];
            options.ContainerName = azureStorage["ContainerName"] ?? "profile-photos";
            options.PublicBaseUrl = azureStorage["PublicBaseUrl"];

            if (long.TryParse(azureStorage["MaxBytes"], out var maxBytes))
            {
                options.MaxBytes = maxBytes;
            }
        });
        services.Configure<PatientFileStorageOptions>(options =>
        {
            var azureStorage = context.Configuration.GetSection("AzureStorage");
            options.ConnectionString = azureStorage["ConnectionString"]
                ?? context.Configuration["AzureWebJobsStorage"];
            options.ContainerName = azureStorage["PatientFilesContainerName"] ?? "patient-files";
            options.PublicBaseUrl = azureStorage["PatientFilesPublicBaseUrl"];

            if (long.TryParse(azureStorage["PatientFileMaxBytes"], out var maxBytes))
            {
                options.MaxBytes = maxBytes;
            }
        });
        services.AddSingleton<SmtpPasswordResetNotificationSender>();
        services.AddSingleton<IProfilePhotoStorage, AzureBlobProfilePhotoStorage>();
        services.AddSingleton<IPatientFileStorage, AzureBlobPatientFileStorage>();
    })
    .Build();

host.Run();
