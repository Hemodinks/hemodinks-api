using System.Threading.RateLimiting;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

namespace HemodinksAPI.Api;

public static partial class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        var defaultAllowedOrigins = new[]
        {
            "http://localhost:3000",
            "http://localhost:5173",
            "http://localhost:8080",
            "https://hemodinks-saude.vercel.app",
            "https://hemodinks-homologacao.vercel.app"
        };

        var configuredAllowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? Array.Empty<string>();

        var allowedOrigins = defaultAllowedOrigins
            .Concat(configuredAllowedOrigins)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return services;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
                Description = "API ASP.NET Core/.NET 10 da Hemodinks, organizada em Domain, Application, Infrastructure e Api. Expoe autenticacao JWT, usuarios, pacientes, dashboard, CBHPM, licencas, agenda de eventos e lembretes. Use o esquema Bearer para chamadas protegidas.",
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

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }
}
