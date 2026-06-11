using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

namespace HemodinksAPI.Api;

public static class ApiApplicationExtensions
{
    public static void UseApiDocumentation(this WebApplication app)
    {
        var documentationEnabled = app.Configuration.GetValue<bool?>("ApiDocumentation:Enabled")
            ?? (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"));

        if (!documentationEnabled)
        {
            return;
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
                .WithTitle("Hemodinks API - Documentacao Interativa")
                .WithOpenApiRoutePattern("/openapi/{documentName}.json")
                .AddPreferredSecuritySchemes("Bearer")
                .DisableAgent();
        }).AllowAnonymous();
    }

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("Iniciando migracao do banco de dados");

            var runMigrations = app.Configuration.GetValue<bool?>("Database:RunMigrationsOnStartup")
                ?? !app.Environment.IsProduction();

            if (runMigrations && dbContext.Database.IsRelational())
            {
                await dbContext.Database.MigrateAsync();
            }
            else if (runMigrations)
            {
                await dbContext.Database.EnsureCreatedAsync();
            }
            else
            {
                logger.LogInformation("Migracao automatica desabilitada para este ambiente");
            }

            logger.LogInformation("Inicializacao do banco de dados concluida");

            var seedCbhpm = app.Configuration.GetValue<bool?>("Seed:CbhpmOnStartup")
                ?? !app.Environment.IsProduction();

            if (seedCbhpm)
            {
                var cbhpmSeeder = scope.ServiceProvider.GetRequiredService<CbhpmSeeder>();
                await cbhpmSeeder.SeedAsync();
            }

            var seedUsers = app.Configuration.GetValue<bool?>("Seed:UsersOnStartup")
                ?? !app.Environment.IsProduction();

            if (seedUsers && !await dbContext.Users.AnyAsync())
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

    public static void MapApiEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", async (
                HealthCheckService healthChecks,
                CancellationToken cancellationToken) =>
            {
                var report = await healthChecks.CheckHealthAsync(cancellationToken);
                var payload = new
                {
                    status = report.Status.ToString(),
                    checkedAt = DateTimeOffset.UtcNow,
                    totalDurationMs = report.TotalDuration.TotalMilliseconds,
                    checks = report.Entries.ToDictionary(
                        entry => entry.Key,
                        entry => new
                        {
                            status = entry.Value.Status.ToString(),
                            description = entry.Value.Description,
                            durationMs = entry.Value.Duration.TotalMilliseconds
                        })
                };

                return report.Status == HealthStatus.Healthy
                    ? Results.Ok(payload)
                    : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithName("HealthCheck")
            .AllowAnonymous();

        app.MapDashboardEndpoints();
        app.MapCbhpmEndpoints();
        app.MapHospitalEndpoints();
        app.MapConvenioEndpoints();
        app.MapUserEndpoints();
        app.MapPacienteEndpoints();
        app.MapLicencaEndpoints();
        app.MapEventEndpoints();
    }
}
