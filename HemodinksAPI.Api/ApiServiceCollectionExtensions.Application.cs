using HemodinksAPI.Application;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Features.ConfiguracoesSistema;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Application.Features.Clinics;
using HemodinksAPI.Application.Features.Financeiro;
using HemodinksAPI.Application.Features.Teams;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Infrastructure.Data.Repositories;
using HemodinksAPI.Infrastructure.HostedServices;
using HemodinksAPI.Infrastructure.PasswordReset;
using HemodinksAPI.Application.Idempotency;
using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Queues;
using HemodinksAPI.Infrastructure.Seeders;
using HemodinksAPI.Infrastructure.Services;
using HemodinksAPI.Infrastructure.Utils;

namespace HemodinksAPI.Api;

public static partial class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IConfiguracaoSistemaRepository, ConfiguracaoSistemaRepository>();
        services.AddScoped<IUserPatientSyncService, UserPatientSyncService>();
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.Configure<FrontendOptions>(configuration.GetSection("Frontend"));
        services.ConfigureAsyncQueueOptions(configuration);
        services.AddAsyncQueueServices(configuration);

        services.AddMemoryCache();
        services.AddScoped<ICbhpmCache, CbhpmCache>();
        services.AddScoped<UserSeeder>();
        services.AddScoped<CbhpmSeeder>();
        services.AddScoped<RequestIdempotencyService>();
        services.AddScoped<IIdempotencyRequestStore, EfIdempotencyRequestStore>();
        services.AddScoped<IPlatformAuditWriter, EfPlatformAuditWriter>();
        services.AddScoped<PlatformAuditService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IEventReminderProcessor, EventReminderProcessor>();
        services.AddScoped<SessionUseCases>();
        services.AddScoped<PublicClinicQueries>();
        services.AddScoped<FinanceiroFileUseCases>();
        services.AddScoped<TeamUseCases>();
        var runEventReminderProcessor = configuration.GetValue<bool?>("EventReminders:RunHostedProcessor")
            ?? !environment.IsProduction();
        if (runEventReminderProcessor)
        {
            services.AddHostedService<EventNotificationHostedService>();
        }
        services.AddApplicationLayer();

        return services;
    }

    private static void ConfigureAsyncQueueOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AsyncQueueOptions>(options =>
        {
            configuration.GetSection("AsyncQueues").Bind(options);
            options.Enabled = configuration.GetValue<bool>("AsyncQueues:Enabled");
            options.PasswordResetEnabled = ResolveAsyncQueueFeatureEnabled(
                configuration,
                "AsyncQueues:PasswordResetEnabled",
                options.Enabled);
            options.FileExportEnabled = ResolveAsyncQueueFeatureEnabled(
                configuration,
                "AsyncQueues:FileExportEnabled",
                options.Enabled);
            options.ConnectionString = configuration["AsyncQueues:ConnectionString"]
                ?? configuration["AzureStorage:ConnectionString"];
        });
    }

    private static void AddAsyncQueueServices(this IServiceCollection services, IConfiguration configuration)
    {
        var asyncQueuesEnabled = configuration.GetValue<bool>("AsyncQueues:Enabled");
        var passwordResetQueueEnabled = ResolveAsyncQueueFeatureEnabled(
            configuration,
            "AsyncQueues:PasswordResetEnabled",
            asyncQueuesEnabled);
        var fileExportQueueEnabled = ResolveAsyncQueueFeatureEnabled(
            configuration,
            "AsyncQueues:FileExportEnabled",
            asyncQueuesEnabled);

        if (passwordResetQueueEnabled || fileExportQueueEnabled)
        {
            services.AddSingleton<IAsyncQueuePublisher, AzureStorageQueuePublisher>();
        }

        if (fileExportQueueEnabled)
        {
            services.AddScoped<IFileExportQueue, AzureFileExportQueue>();
        }
        else
        {
            services.AddScoped<IFileExportQueue, DisabledFileExportQueue>();
        }

        if (HasValidPasswordResetFunctionConfiguration(configuration))
        {
            services.Configure<PasswordResetFunctionOptions>(configuration.GetSection("PasswordResetFunctions"));
            services.AddHttpClient(nameof(PasswordResetFunctionClient));
            services.AddSingleton<PasswordResetFunctionClient>();
            services.AddScoped<IPasswordResetNotificationTransport, FunctionBackedPasswordResetNotificationSender>();
        }

        if (passwordResetQueueEnabled)
        {
            services.AddScoped<IPasswordResetNotificationTransport, AzureQueuePasswordResetNotificationSender>();
        }

        services.AddScoped<IPasswordResetNotificationTransport, SmtpPasswordResetNotificationSender>();
        services.AddScoped<IPasswordResetNotificationSender, FallbackPasswordResetNotificationSender>();
    }

    private static bool ResolveAsyncQueueFeatureEnabled(
        IConfiguration configuration,
        string featureKey,
        bool fallbackValue)
    {
        return configuration.GetValue<bool?>(featureKey) ?? fallbackValue;
    }

    private static bool HasValidPasswordResetFunctionConfiguration(IConfiguration configuration)
    {
        var baseUrl = configuration["PasswordResetFunctions:BaseUrl"];
        var functionKey = configuration["PasswordResetFunctions:FunctionKey"];

        return HasValidAbsoluteHttpUrl(baseUrl)
            && !string.IsNullOrWhiteSpace(functionKey);
    }

    private static bool HasValidAbsoluteHttpUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return false;
        }

        var normalizedBaseUrl = baseUrl.Trim().TrimEnd('/');
        normalizedBaseUrl = normalizedBaseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase)
            ? $"{normalizedBaseUrl}/"
            : $"{normalizedBaseUrl}/api/";

        return Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
