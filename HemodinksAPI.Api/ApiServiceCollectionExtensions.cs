using System.Text;
using HemodinksAPI.Application;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Authentication;
using HemodinksAPI.Infrastructure.Authorization;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Seeders;
using HemodinksAPI.Infrastructure.Services;
using HemodinksAPI.Infrastructure.Storage;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Threading.RateLimiting;

namespace HemodinksAPI.Api;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(defaultConnection))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options
                .UseSqlServer(defaultConnection,
                    sqlServerOptionsAction: sqlOptions =>
                    {
                        sqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
                        sqlOptions.EnableRetryOnFailure();
                    }));
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(
                "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
            ?? throw new InvalidOperationException("JwtSettings nao configurado");

        if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
        {
            throw new InvalidOperationException("JwtSettings:SecretKey must be configured.");
        }

        if (Encoding.UTF8.GetByteCount(jwtSettings.SecretKey) < 32)
        {
            throw new InvalidOperationException("JwtSettings:SecretKey must contain at least 32 bytes.");
        }

        if (string.IsNullOrWhiteSpace(jwtSettings.Issuer))
        {
            throw new InvalidOperationException("JwtSettings:Issuer must be configured.");
        }

        if (string.IsNullOrWhiteSpace(jwtSettings.Audience))
        {
            throw new InvalidOperationException("JwtSettings:Audience must be configured.");
        }

        services.AddSingleton(jwtSettings);
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        var key = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);
        services.AddAuthentication(x =>
        {
            x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(x =>
        {
            x.RequireHttpsMetadata = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
            x.SaveToken = true;
            x.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Administrador", policy =>
                policy.RequireClaim("perfilId", Perfil.AdministradorId.ToString()));

            options.AddPolicy(LicencaPolicies.DashboardVisualizar, policy =>
                policy.Requirements.Add(new LicencaFeatureRequirement(LicencaFeatures.DashboardVisualizar)));

            options.AddPolicy(LicencaPolicies.PacientesVisualizar, policy =>
                policy.Requirements.Add(new LicencaFeatureRequirement(LicencaFeatures.PacientesVisualizar)));

            options.AddPolicy(LicencaPolicies.PacientesGerenciar, policy =>
                policy.Requirements.Add(new LicencaFeatureRequirement(LicencaFeatures.PacientesGerenciar)));

            options.AddPolicy(LicencaPolicies.CbhpmConsultar, policy =>
                policy.Requirements.Add(new LicencaFeatureRequirement(LicencaFeatures.CbhpmConsultar)));
        });

        return services;
    }

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
                policy
                    .WithOrigins(allowedOrigins)
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

    public static IServiceCollection AddStorage(this IServiceCollection services, IConfiguration configuration)
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

        services.AddSingleton<IProfilePhotoStorage, AzureBlobProfilePhotoStorage>();
        services.AddSingleton<IPatientFileStorage, AzureBlobPatientFileStorage>();

        return services;
    }

    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IUserPatientSyncService, UserPatientSyncService>();
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.Configure<FrontendOptions>(configuration.GetSection("Frontend"));
        services.AddScoped<IPasswordResetNotificationSender, SmtpPasswordResetNotificationSender>();
        services.Configure<PasswordResetOptions>(options =>
        {
            configuration.GetSection("PasswordReset").Bind(options);
            var useEmail = configuration.GetValue<bool?>("COM_EMAIL")
                ?? configuration.GetValue<bool?>("PASSWORD_RESET_USE_EMAIL")
                ?? configuration.GetValue<bool?>("PASSWORD_RESET_COM_EMAIL")
                ?? configuration.GetValue<bool?>("com-email")
                ?? configuration.GetValue<bool?>("PasswordReset:com-email")
                ?? configuration.GetValue<bool?>("PasswordReset:ComEmail")
                ?? configuration.GetValue<bool?>("PasswordReset:UseEmail");

            if (useEmail.HasValue)
            {
                options.UseEmail = useEmail.Value;
            }

            if (!environment.IsProduction() && !configuration.GetSection("PasswordReset").Exists())
            {
                options.ExposeTokenInResponse = true;
            }
        });
        services.AddMemoryCache();
        services.AddScoped<ICbhpmCache, CbhpmCache>();
        services.AddScoped<UserSeeder>();
        services.AddScoped<CbhpmSeeder>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IEventReminderProcessor, EventReminderProcessor>();
        services.AddHostedService<HemodinksAPI.Infrastructure.HostedServices.EventNotificationHostedService>();
        services.AddApplicationLayer();

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

        return services;
    }
}
