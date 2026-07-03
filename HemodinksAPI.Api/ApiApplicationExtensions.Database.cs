using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public static partial class ApiApplicationExtensions
{
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
            var isRelational = dbContext.Database.IsRelational();
            var pendingMigrations = isRelational
                ? (await dbContext.Database.GetPendingMigrationsAsync()).ToList()
                : [];

            if (pendingMigrations.Count > 0)
            {
                logger.LogWarning(
                    "Encontradas {Count} migration(s) pendente(s): {Migrations}",
                    pendingMigrations.Count,
                    pendingMigrations);
            }
            else if (isRelational)
            {
                logger.LogInformation("Nenhuma migration pendente encontrada");
            }

            if (runMigrations && isRelational)
            {
                if (pendingMigrations.Count > 0)
                {
                    await dbContext.Database.MigrateAsync();
                    logger.LogInformation("Migrations pendentes aplicadas com sucesso");
                }
            }
            else if (runMigrations)
            {
                await dbContext.Database.EnsureCreatedAsync();
            }
            else
            {
                logger.LogWarning("Migracao automatica desabilitada. Se tabelas estiverem faltando, defina Database:RunMigrationsOnStartup=true");
            }

            logger.LogInformation("Inicializacao do banco de dados concluida");

            await SeedReferenceDataAsync(app, scope.ServiceProvider, dbContext, logger);
            await SyncPatientRecordsAsync(dbContext, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao processar migracao ou seed do banco de dados");
            throw;
        }
    }

    private static async Task SeedReferenceDataAsync(
        WebApplication app,
        IServiceProvider services,
        AppDbContext dbContext,
        ILogger logger)
    {
        var seedCbhpm = app.Configuration.GetValue<bool?>("Seed:CbhpmOnStartup")
            ?? !app.Environment.IsProduction();

        if (seedCbhpm)
        {
            var cbhpmSeeder = services.GetRequiredService<CbhpmSeeder>();
            await cbhpmSeeder.SeedAsync();
        }

        var seedUsers = app.Configuration.GetValue<bool?>("Seed:UsersOnStartup")
            ?? !app.Environment.IsProduction();

        if (seedUsers && !await dbContext.Users.AnyAsync())
        {
            logger.LogInformation("Iniciando seed de dados");
            var seeder = services.GetRequiredService<UserSeeder>();
            var users = seeder.GenerateUsers();
            dbContext.Users.AddRange(users);
            await dbContext.SaveChangesAsync();
            logger.LogInformation("Seed de {Count} usuarios concluido com sucesso", users.Count);
        }
    }

    private static async Task SyncPatientRecordsAsync(AppDbContext dbContext, ILogger logger)
    {
        var patientUsersWithoutRecord = await dbContext.Users
            .Where(user => user.PerfilId == Perfil.PacientesId
                && !dbContext.Pacientes.Any(paciente => paciente.UserId == user.Id))
            .ToListAsync();

        if (patientUsersWithoutRecord.Count == 0)
        {
            return;
        }

        dbContext.Pacientes.AddRange(patientUsersWithoutRecord.Select(user => new Paciente
        {
            ClinicaId = user.ClinicaId,
            UserId = user.Id,
            NomePaciente = user.Nome
        }));

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Sincronizados {Count} cadastros de pacientes", patientUsersWithoutRecord.Count);
    }
}
