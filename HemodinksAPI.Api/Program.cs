using System.Text;
using HemodinksAPI.Api;
using HemodinksAPI.Api.Authentication;
using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Cbhpm;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Seeders;
using HemodinksAPI.Api.Services;
using HemodinksAPI.Api.Storage;
using HemodinksAPI.Api.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/hemodinks-api-.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentUserName()
    .Enrich.WithThreadId()
    .CreateLogger();

builder.Host.UseSerilog();

var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnection))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(defaultConnection,
        sqlServerOptionsAction: sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
        }));

var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
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

builder.Services.AddSingleton(jwtSettings);

var key = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);
builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = false;
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Administrador", policy =>
        policy.RequireClaim("perfilId", Perfil.AdministradorId.ToString()));
});

var defaultAllowedOrigins = new[]
{
    "http://localhost:3000",
    "http://localhost:5173",
    "http://localhost:8080",
    "https://hemodinks-saude.vercel.app"
};

var configuredAllowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? Array.Empty<string>();

var allowedOrigins = defaultAllowedOrigins
    .Concat(configuredAllowedOrigins)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IUserPatientSyncService, UserPatientSyncService>();
builder.Services.Configure<ProfilePhotoStorageOptions>(builder.Configuration.GetSection("AzureStorage"));
builder.Services.Configure<PatientFileStorageOptions>(options =>
{
    var azureStorage = builder.Configuration.GetSection("AzureStorage");
    options.ConnectionString = azureStorage["ConnectionString"];
    options.ContainerName = azureStorage["PatientFilesContainerName"] ?? "patient-files";
    options.PublicBaseUrl = azureStorage["PatientFilesPublicBaseUrl"];

    if (long.TryParse(azureStorage["PatientFileMaxBytes"], out var maxBytes))
    {
        options.MaxBytes = maxBytes;
    }
});
builder.Services.AddSingleton<IProfilePhotoStorage, AzureBlobProfilePhotoStorage>();
builder.Services.AddSingleton<IPatientFileStorage, AzureBlobPatientFileStorage>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICbhpmCache, CbhpmCache>();
builder.Services.AddScoped<UserSeeder>();
builder.Services.AddScoped<CbhpmSeeder>();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Hemodinks API",
        Version = "v1",
        Description = "API para gestao de usuarios, pacientes, arquivos, dashboard e consulta CBHPM com cache em memoria.",
        Contact = new OpenApiContact
        {
            Name = "Hemodinks",
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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Iniciando migracao do banco de dados");
        dbContext.Database.Migrate();
        logger.LogInformation("Migracao do banco de dados concluida com sucesso");

        var cbhpmSeeder = scope.ServiceProvider.GetRequiredService<CbhpmSeeder>();
        await cbhpmSeeder.SeedAsync();

        if (!dbContext.Users.Any())
        {
            logger.LogInformation("Iniciando seed de dados");
            var seeder = scope.ServiceProvider.GetRequiredService<UserSeeder>();
            var users = seeder.GenerateUsers();
            dbContext.Users.AddRange(users);
            await dbContext.SaveChangesAsync();
            logger.LogInformation("Seed de {Count} usuarios concluido com sucesso", users.Count);
        }

        var patientUsersWithoutRecord = await dbContext.Users
            .Where(user => user.PerfilId == Perfil.PacientesId
                && !dbContext.Pacientes.Any(paciente => paciente.UserId == user.Id))
            .ToListAsync();

        if (patientUsersWithoutRecord.Count > 0)
        {
            dbContext.Pacientes.AddRange(patientUsersWithoutRecord.Select(user => new Paciente
            {
                UserId = user.Id,
                NomePaciente = user.Nome
            }));

            await dbContext.SaveChangesAsync();
            logger.LogInformation("Sincronizados {Count} cadastros de pacientes", patientUsersWithoutRecord.Count);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao processar migracao ou seed do banco de dados");
        throw;
    }
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Hemodinks API v1");
    options.RoutePrefix = "swagger";
});
app.MapSwagger("/openapi/{documentName}.json").AllowAnonymous();
app.MapScalarApiReference("/scalar", options =>
{
    options
        .WithTitle("Hemodinks API")
        .WithOpenApiRoutePattern("/openapi/{documentName}.json")
        .AddPreferredSecuritySchemes("Bearer")
        .DisableAgent();
}).AllowAnonymous();

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck")
    .AllowAnonymous();

app.MapDashboardEndpoints();
app.MapCbhpmEndpoints();
app.MapUserEndpoints();
app.MapPacienteEndpoints();

app.Run();
