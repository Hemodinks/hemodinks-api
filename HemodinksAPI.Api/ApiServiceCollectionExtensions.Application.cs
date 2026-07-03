using HemodinksAPI.Application;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Features.ConfiguracoesSistema;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Infrastructure.Data.Repositories;
using HemodinksAPI.Infrastructure.HostedServices;
using HemodinksAPI.Infrastructure.PasswordReset;
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
        services.ConfigurePasswordResetOptions(configuration, environment);
        services.AddAsyncQueueServices(configuration);

        services.AddMemoryCache();
        services.AddScoped<ICbhpmCache, CbhpmCache>();
        services.AddScoped<UserSeeder>();
        services.AddScoped<CbhpmSeeder>();
        services.AddScoped<RequestIdempotencyService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IEventReminderProcessor, EventReminderProcessor>();
        services.AddHostedService<EventNotificationHostedService>();
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
            services.AddScoped<IPasswordResetNotificationSender, FunctionBackedPasswordResetNotificationSender>();
            return;
        }

        if (passwordResetQueueEnabled)
        {
            services.AddScoped<IPasswordResetNotificationSender, AzureQueuePasswordResetNotificationSender>();
            return;
        }

        services.AddScoped<IPasswordResetNotificationSender, SmtpPasswordResetNotificationSender>();
    }

    private static void ConfigurePasswordResetOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.Configure<PasswordResetOptions>(options =>
        {
            configuration.GetSection("PasswordReset").Bind(options);
            var useEmail = ResolvePasswordResetUseEmail(configuration);

            if (useEmail.HasValue)
            {
                options.UseEmail = useEmail.Value;
            }

            if (!environment.IsProduction() && !configuration.GetSection("PasswordReset").Exists())
            {
                options.ExposeTokenInResponse = true;
            }
        });
    }

    private static bool? ResolvePasswordResetUseEmail(IConfiguration configuration)
    {
        return configuration.GetValue<bool?>("COM_EMAIL")
            ?? configuration.GetValue<bool?>("PASSWORD_RESET_USE_EMAIL")
            ?? configuration.GetValue<bool?>("PASSWORD_RESET_COM_EMAIL")
            ?? configuration.GetValue<bool?>("com-email")
            ?? configuration.GetValue<bool?>("PasswordReset:com-email")
            ?? configuration.GetValue<bool?>("PasswordReset:ComEmail")
            ?? configuration.GetValue<bool?>("PasswordReset:UseEmail");
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
