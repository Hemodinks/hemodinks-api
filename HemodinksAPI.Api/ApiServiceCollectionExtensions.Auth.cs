using System.Text;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Authentication;
using HemodinksAPI.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace HemodinksAPI.Api;

public static partial class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var jwtSettings = LoadJwtSettings(configuration);

        services.AddSingleton(jwtSettings);
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        var key = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
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

        services.AddAuthorization(ConfigureAuthorizationPolicies);
        return services;
    }

    private static JwtSettings LoadJwtSettings(IConfiguration configuration)
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

        return jwtSettings;
    }

    private static void ConfigureAuthorizationPolicies(AuthorizationOptions options)
    {
        options.AddPolicy("Administrador", policy =>
            policy.RequireClaim("perfilId", Perfil.AdministradorId.ToString(), Perfil.SuperAdministradorId.ToString()));

        options.AddPolicy("SuperAdministrador", policy =>
            policy.RequireClaim("perfilId", Perfil.SuperAdministradorId.ToString()));

        options.AddPolicy("GrupoMedicoCadastrar", policy =>
            policy.RequireClaim("perfilId", Perfil.AdministradorId.ToString(), Perfil.SuperAdministradorId.ToString(), Perfil.ControllerId.ToString()));

        options.AddPolicy("PacienteCadastrar", policy =>
            policy.RequireClaim("perfilId", Perfil.AdministradorId.ToString(), Perfil.SuperAdministradorId.ToString(), Perfil.ControllerId.ToString(), Perfil.MedicosId.ToString()));

        options.AddPolicy("PacienteArquivosGerenciar", policy =>
            policy.RequireClaim("perfilId", Perfil.AdministradorId.ToString(), Perfil.SuperAdministradorId.ToString(), Perfil.MedicosId.ToString(), Perfil.ControllerId.ToString()));

        options.AddPolicy("PacienteEditar", policy =>
            policy.RequireClaim("perfilId", Perfil.AdministradorId.ToString(), Perfil.SuperAdministradorId.ToString(), Perfil.MedicosId.ToString(), Perfil.ControllerId.ToString()));

        options.AddPolicy("PacienteObservacaoGerenciar", policy =>
            policy.RequireClaim("perfilId", Perfil.AdministradorId.ToString(), Perfil.SuperAdministradorId.ToString(), Perfil.MedicosId.ToString(), Perfil.ControllerId.ToString()));

        options.AddPolicy("FaturamentoMedicoVisualizar", policy =>
            policy.RequireClaim("perfilId", Perfil.AdministradorId.ToString(), Perfil.SuperAdministradorId.ToString(), Perfil.MedicosId.ToString(), Perfil.ControllerId.ToString()));

        options.AddPolicy(LicencaPolicies.DashboardVisualizar, policy =>
            policy.Requirements.Add(new LicencaFeatureRequirement(LicencaFeatures.DashboardVisualizar)));

        options.AddPolicy(LicencaPolicies.PacientesVisualizar, policy =>
            policy.RequireClaim("perfilId", Perfil.AdministradorId.ToString(), Perfil.SuperAdministradorId.ToString(), Perfil.MedicosId.ToString(), Perfil.ControllerId.ToString()));

        options.AddPolicy(LicencaPolicies.PacientesGerenciar, policy =>
            policy.Requirements.Add(new LicencaFeatureRequirement(LicencaFeatures.PacientesGerenciar)));

        options.AddPolicy(LicencaPolicies.CbhpmConsultar, policy =>
            policy.Requirements.Add(new LicencaFeatureRequirement(LicencaFeatures.CbhpmConsultar)));
    }
}
