using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Seeders;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Application.Authentication;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

internal static class DatabaseStartupInitializer
{
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinicaContext = scope.ServiceProvider.GetRequiredService<ClinicaContext>();
        clinicaContext.SetPlatformScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("Iniciando migracao do banco de dados");

            var runMigrations = ShouldRunMigrations(app.Environment, app.Configuration);
            var isRelational = dbContext.Database.IsRelational();
            var pendingMigrations = isRelational
                ? (await dbContext.Database.GetPendingMigrationsAsync()).ToList()
                : [];

            LogPendingMigrations(logger, isRelational, pendingMigrations);

            await ApplyDatabaseSchemaAsync(dbContext, logger, runMigrations, isRelational, pendingMigrations);

            logger.LogInformation("Inicializacao do banco de dados concluida");

            await SeedReferenceDataAsync(app, scope.ServiceProvider, dbContext, logger);
            var runMaintenance = app.Configuration.GetValue<bool?>("Database:RunMaintenanceOnStartup")
                ?? app.Environment.IsDevelopment();
            if (runMaintenance)
            {
                await ProvisionSuperAdministratorsAsync(app.Configuration, dbContext, logger);
                await SynchronizeGlobalIdentitiesAsync(dbContext, logger);
                await SyncPatientRecordsAsync(dbContext, logger);
            }
            else
            {
                logger.LogInformation("Manutencao e backfills de startup desabilitados");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao processar migracao ou seed do banco de dados");
            throw;
        }
    }

    internal static bool ShouldRunMigrations(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (environment.IsProduction())
        {
            return false;
        }

        return configuration.GetValue<bool?>("Database:RunMigrationsOnStartup")
            ?? environment.IsDevelopment();
    }

    private static async Task SynchronizeGlobalIdentitiesAsync(AppDbContext dbContext, ILogger logger)
    {
        var users = await dbContext.Users
            .IgnoreQueryFilters()
            .OrderBy(item => item.Id)
            .ToListAsync();

        foreach (var user in users)
        {
            var membership = await GlobalIdentityService.EnsureForUserAsync(dbContext, user, CancellationToken.None);
            membership.PerfilId = user.PerfilId;
            membership.Ativo = user.Ativo;
            membership.DataAtualizacao = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Identidades globais sincronizadas para {Count} usuarios locais", users.Count);
    }

    private static async Task ProvisionSuperAdministratorsAsync(
        IConfiguration configuration,
        AppDbContext dbContext,
        ILogger logger)
    {
        var configuredEmails = configuration.GetSection("Platform:SuperAdminEmails")
            .Get<string[]>()
            ?.Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (configuredEmails.Count == 0)
        {
            return;
        }

        var clinicaIds = await dbContext.Clinicas
            .Where(item => item.Ativa)
            .Select(item => item.Id)
            .ToListAsync();

        foreach (var email in configuredEmails)
        {
            var existingUsers = await dbContext.Users
                .IgnoreQueryFilters()
                .Where(item => item.Email == email)
                .OrderBy(item => item.ClinicaId == Clinica.DefaultId ? 0 : 1)
                .ThenBy(item => item.Id)
                .ToListAsync();

            var source = existingUsers.FirstOrDefault();
            if (source == null)
            {
                logger.LogWarning("Superadministrador configurado nao encontrado: {Email}", email);
                continue;
            }

            foreach (var user in existingUsers)
            {
                user.PerfilId = Perfil.SuperAdministradorId;
            }

            foreach (var clinicaId in clinicaIds.Except(existingUsers.Select(item => item.ClinicaId)))
            {
                dbContext.Users.Add(new User
                {
                    ClinicaId = clinicaId,
                    Nome = source.Nome,
                    Email = source.Email,
                    Telefone = $"+559{clinicaId:00000000000}",
                    Senha = source.Senha,
                    DataNascimento = source.DataNascimento,
                    DataCadastro = DateTime.UtcNow,
                    Ativo = true,
                    PrecisaTrocarSenha = source.PrecisaTrocarSenha,
                    PerfilId = Perfil.SuperAdministradorId
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static void LogPendingMigrations(
        ILogger logger,
        bool isRelational,
        List<string> pendingMigrations)
    {
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
    }

    private static async Task ApplyDatabaseSchemaAsync(
        AppDbContext dbContext,
        ILogger logger,
        bool runMigrations,
        bool isRelational,
        List<string> pendingMigrations)
    {
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
    }

    private static async Task SeedReferenceDataAsync(
        WebApplication app,
        IServiceProvider services,
        AppDbContext dbContext,
        ILogger logger)
    {
        var seedCbhpm = app.Configuration.GetValue<bool?>("Seed:CbhpmOnStartup")
            ?? app.Environment.IsDevelopment();

        if (seedCbhpm)
        {
            var cbhpmSeeder = services.GetRequiredService<CbhpmSeeder>();
            await cbhpmSeeder.SeedAsync();
        }

        var seedUsers = app.Configuration.GetValue<bool?>("Seed:UsersOnStartup")
            ?? app.Environment.IsDevelopment();

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
