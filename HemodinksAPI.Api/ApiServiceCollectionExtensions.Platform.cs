using System.Threading.RateLimiting;
using System.Net;
using HemodinksAPI.Application.Features.Licencas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;

namespace HemodinksAPI.Api;

public static partial class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddProxyForwarding(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("ForwardedHeaders");
        var enabled = section.GetValue<bool>("Enabled");
        var trustAnyImmediateProxy = section.GetValue<bool>("TrustAnyImmediateProxy");
        var forwardLimit = section.GetValue<int?>("ForwardLimit") ?? 1;

        if (forwardLimit < 1)
        {
            throw new InvalidOperationException("ForwardedHeaders:ForwardLimit deve ser maior que zero.");
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = enabled
                ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                : ForwardedHeaders.None;
            options.ForwardLimit = forwardLimit;

            if (!enabled)
            {
                return;
            }

            if (trustAnyImmediateProxy)
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                return;
            }

            var configuredProxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
            var configuredNetworks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];

            if (configuredProxies.Length > 0 || configuredNetworks.Length > 0)
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }

            foreach (var configuredProxy in configuredProxies)
            {
                if (!IPAddress.TryParse(configuredProxy, out var proxy))
                {
                    throw new InvalidOperationException($"Proxy confiavel invalido em ForwardedHeaders:KnownProxies: {configuredProxy}");
                }

                options.KnownProxies.Add(proxy);
            }

            foreach (var configuredNetwork in configuredNetworks)
            {
                if (!System.Net.IPNetwork.TryParse(configuredNetwork, out var network))
                {
                    throw new InvalidOperationException($"Rede confiavel invalida em ForwardedHeaders:KnownNetworks: {configuredNetwork}");
                }

                options.KnownIPNetworks.Add(network);
            }
        });

        return services;
    }

    public static IServiceCollection AddFrontendCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? [];

        allowedOrigins = allowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowedOrigins.Length == 0)
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one trusted origin.");
        }

        foreach (var origin in allowedOrigins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || uri.AbsolutePath != "/")
            {
                throw new InvalidOperationException($"Invalid CORS origin: {origin}");
            }

            if (environment.IsProduction()
                && (uri.Scheme != Uri.UriSchemeHttps
                    || uri.IsLoopback
                    || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Production CORS origin must use HTTPS and cannot target localhost: {origin}");
            }
        }

        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("Login", context =>
            {
                var clinic = context.Request.Headers[ClinicaResolutionService.ClinicaSlugHeaderName].ToString();
                var partitionKey = $"{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}:{clinic}";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
            options.AddPolicy("PasswordReset", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
            options.AddPolicy("PublicClinics", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        return services;
    }

    public static IServiceCollection AddLicensing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LicencaOptions>(configuration.GetSection("Licensing"));
        services.AddScoped<ILicencaService, LicencaService>();
        services.AddScoped<IAuthorizationHandler, LicencaFeatureAuthorizationHandler>();

        return services;
    }

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Hemodinks API",
                Version = "v1",
                Description = "API ASP.NET Core/.NET 10 da Hemodinks, organizada em Domain, Application, Infrastructure e Api. Expoe autenticacao JWT, usuarios, pacientes, dashboard, CBHPM, licencas, agenda de eventos e lembretes. O reset por email pode sair por Function HTTP, fila Azure ou SMTP, conforme configuracao. Use o esquema Bearer para chamadas protegidas.",
                Contact = new OpenApiContact
                {
                    Name = "GM Tech Solutions - Hemodinks",
                    Email = "gmarcone@gmail.com"
                }
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Informe o token JWT no formato: Bearer {token}"
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
            });
        });

        return services;
    }
}
