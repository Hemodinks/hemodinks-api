using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Hemodinks.SchemaGuard;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace HemodinksAPI.Tests;

public sealed class FullMigrationReplaySqlServerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "SqlServer")]
    public async Task FullReplay_AppliesAllMigrationsAndCanRunTwice(bool idempotentScript)
    {
        SqlServerTestConnection.RequireConfigured();
        var name = $"HemodinksFullReplay_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlServerTestConnection.Create(name), sql => sql.CommandTimeout(120)).Options;
        await using var db = new AppDbContext(options, ClinicaContextFactory.CreatePlatform());
        try
        {
            await db.GetService<IRelationalDatabaseCreator>().CreateAsync();
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT CAST(FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS int) AS [Value]").SingleAsync());
            var migrator = db.GetService<IMigrator>();
            Assert.NotEmpty(db.Database.GetMigrations());
            var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            string[]? schema = null;
            string? data = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (idempotentScript) await ExecuteScript(db, script);
                else await migrator.MigrateAsync();
                Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
                Assert.Equal(8, await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS [Value] FROM sys.fulltext_indexes").SingleAsync());
                if (attempt == 0)
                {
                    await SeedAndCheckTenantIsolation(db);
                    schema = await Schema(db);
                    data = await Data(db);
                    output.WriteLine("Schema SHA256: " + Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(string.Join('\n', schema)))));
                }
                else
                {
                    Assert.Equal(schema, await Schema(db));
                    Assert.Equal(data, await Data(db));
                }
                Assert.True(await SchemaHistoryGuard.CheckAsync(db, CancellationToken.None));
            }
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData("20260601163622_ResetUsersToDefaultPassword")]
    [InlineData("20260721160019_AddPartialClinicPlanModules")]
    [InlineData("20260810185452_Schema_AddFullTextSearchIndexes")]
    [Trait("Category", "SqlServer")]
    public async Task FullScript_UpgradesPreviouslyAppliedMigrationPrefixes(string prefix)
    {
        SqlServerTestConnection.RequireConfigured();
        await using var db = CreateContext();
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(prefix);
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Equal(prefix, applied.Last());
            var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            await ExecuteScript(db, script);
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(applied, (await db.Database.GetAppliedMigrationsAsync()).Take(applied.Length));
            await SeedAndCheckTenantIsolation(db);
            var schema = await Schema(db);
            var data = await Data(db);
            await ExecuteScript(db, script);
            Assert.Equal(schema, await Schema(db));
            Assert.Equal(data, await Data(db));
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task FullText_RollbackAndReapplyPreservesDataAndSchema()
    {
        SqlServerTestConnection.RequireConfigured();
        await using var db = CreateContext();
        const string beforeFullText = "20260805004914_AllowSharedTeamMemberEmails";
        const string fullText = "20260810185452_Schema_AddFullTextSearchIndexes";
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(beforeFullText);
            var originalSchema = await Schema(db);
            var originalData = await Data(db);
            await ExecuteScript(db, migrator.GenerateScript(beforeFullText, fullText,
                MigrationsSqlGenerationOptions.Idempotent));
            Assert.Equal(8, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.fulltext_indexes").SingleAsync());
            var expandedSchema = await Schema(db);
            await ExecuteScript(db, migrator.GenerateScript(fullText, beforeFullText));
            Assert.Equal(originalSchema, await Schema(db));
            Assert.Equal(originalData, await Data(db));
            Assert.Equal(beforeFullText, (await db.Database.GetAppliedMigrationsAsync()).Last());
            await migrator.MigrateAsync(fullText);
            Assert.Equal(expandedSchema, await Schema(db));
            Assert.Equal(originalData, await Data(db));
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(SqlServerTestConnection.Create($"HemodinksFullReplay_{Guid.NewGuid():N}"),
            sql => sql.CommandTimeout(120)).Options, ClinicaContextFactory.CreatePlatform());

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task HistoricalBackfills_PreserveExistingRowsAndTenantOwnership()
    {
        SqlServerTestConnection.RequireConfigured();
        await using var db = CreateContext();
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260601163622_ResetUsersToDefaultPassword");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Users (Nome, Email, Telefone, Senha, DataNascimento)
                VALUES (N'Synthetic legacy patient', N'legacy-replay@test.local', N'+5511999980001', N'test-hash', '1990-01-01');
                """);
            await Upgrade("20260601180816_AddUserProfiles");
            Assert.Equal(2, await ScalarInt("SELECT PerfilId AS [Value] FROM Users WHERE Email = N'legacy-replay@test.local'"));
            await db.Database.ExecuteSqlRawAsync("UPDATE Users SET PerfilId = 3 WHERE Email = N'legacy-replay@test.local';");
            await Upgrade("20260601200537_AddPatientsAndCpf");
            Assert.Equal(1, await ScalarInt("SELECT COUNT(*) AS [Value] FROM Pacientes WHERE NomePaciente = N'Synthetic legacy patient'"));
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Users (Nome, Email, Telefone, Senha, DataNascimento, PerfilId)
                VALUES (N'Synthetic legacy doctor', N'legacy-doctor@test.local', N'+5511999980002', N'test-hash', '1990-01-01', 2);
                UPDATE Pacientes SET Medico = N'Synthetic legacy doctor', Convenio = N'Amil',
                    Data = '2026-06-15', Pagamento = N'100,00'
                WHERE NomePaciente = N'Synthetic legacy patient';
                """);
            await Upgrade("20260703201553_AddClinicasAndClinicaIds");
            Assert.Equal(1, await ScalarInt("""
                SELECT COUNT(*) AS [Value] FROM Pacientes p JOIN Users u ON u.Id = p.MedicoUserId
                WHERE p.NomePaciente = N'Synthetic legacy patient' AND u.Email = N'legacy-doctor@test.local'
                    AND p.ClinicaId = 1 AND u.ClinicaId = 1 AND p.ConvenioId = 1
                """));
            await Upgrade("20260713160359_AddFaturamentoMedicoCompetencia");
            Assert.Equal(1, await ScalarInt("""
                SELECT COUNT(*) AS [Value] FROM FaturamentosMedicos f JOIN Pacientes p ON p.Id = f.PacienteId
                WHERE p.NomePaciente = N'Synthetic legacy patient' AND f.ClinicaId = 1
                    AND f.CompetenciaInicio = '2026-06-01' AND f.CompetenciaFinal = '2026-06-30'
                """));
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO IdempotencyRequests (Operation, Scope, IdempotencyKey, RequestHash, State)
                VALUES (N'replay', N'clinic:1:user', N'legacy-replay', N'synthetic', N'Completed');
                INSERT INTO Clinicas (Nome, Slug) VALUES (N'Synthetic legacy B', N'legacy-replay-b');
                INSERT INTO IdempotencyRequests (Operation, Scope, IdempotencyKey, RequestHash, State)
                SELECT N'replay', CONCAT(N'clinic:', Id, N':user'), N'legacy-replay-b', N'synthetic', N'Completed'
                FROM Clinicas WHERE Slug = N'legacy-replay-b';
                UPDATE ConfiguracoesSistema SET NomeEmpresa = N'Synthetic legacy clinic', FotoEmpresa = N'/synthetic-logo.png'
                WHERE ClinicaId = 1;
                """);
            await Upgrade("20260721145022_MoveClinicBrandingToClinicCrud");
            Assert.Equal(1, await ScalarInt("SELECT ClinicaId AS [Value] FROM IdempotencyRequests WHERE IdempotencyKey = N'legacy-replay'"));
            Assert.Equal(1, await ScalarInt("SELECT COUNT(*) AS [Value] FROM IdempotencyRequests r JOIN Clinicas c ON c.Id = r.ClinicaId WHERE r.IdempotencyKey = N'legacy-replay-b' AND c.Slug = N'legacy-replay-b' AND c.Id <> 1"));
            Assert.Equal(1, await ScalarInt("SELECT COUNT(*) AS [Value] FROM Clinicas WHERE Id = 1 AND TrialAte IS NOT NULL AND Nome = N'Synthetic legacy clinic' AND FotoClinica = N'/synthetic-logo.png'"));
            Assert.Equal(1, await ScalarInt("SELECT COUNT(*) AS [Value] FROM Clinicas WHERE Slug = N'legacy-replay-b' AND Nome = N'Synthetic legacy B' AND FotoClinica IS NULL"));
            var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            await ExecuteScript(db, script);
            Assert.Equal(1, await ScalarInt("SELECT ClinicaId AS [Value] FROM Pacientes WHERE NomePaciente = N'Synthetic legacy patient'"));
            var schema = await Schema(db);
            var data = await Data(db);
            await ExecuteScript(db, script);
            Assert.Equal(schema, await Schema(db));
            Assert.Equal(data, await Data(db));

            Task Upgrade(string target) => ExecuteScript(db, migrator.GenerateScript(
                toMigration: target, options: MigrationsSqlGenerationOptions.Idempotent));
            Task<int> ScalarInt(string sql) => db.Database.SqlQueryRaw<int>(sql).SingleAsync();
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private static async Task SeedAndCheckTenantIsolation(AppDbContext db)
    {
        var clinic = new Clinica { Nome = "Synthetic replay B", Slug = "replay-b" };
        db.Clinicas.Add(clinic);
        var local = new User { ClinicaId = 1, Nome = "Synthetic replay A", Email = "replay-a@test.local",
            Telefone = "+5511999990001", Senha = "test-hash", PerfilId = Perfil.MedicosId };
        db.Users.Add(local);
        await db.SaveChangesAsync();
        var remote = new User { ClinicaId = clinic.Id, Nome = "Synthetic replay B", Email = "replay-b@test.local",
            Telefone = "+5511999990002", Senha = "test-hash", PerfilId = Perfil.MedicosId };
        db.Users.Add(remote);
        var patient = new Paciente { ClinicaId = 1, UserId = local.Id, NomePaciente = "Synthetic replay patient" };
        db.Pacientes.Add(patient);
        await db.SaveChangesAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options;
        await using var clinicA = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());
        var tenantB = new ClinicaContext();
        tenantB.SetCurrent(clinic.Id, clinic.Slug);
        await using var clinicB = new AppDbContext(options, tenantB);
        Assert.True(await clinicA.Pacientes.AnyAsync(x => x.Id == patient.Id));
        Assert.False(await clinicB.Pacientes.AnyAsync(x => x.Id == patient.Id));
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Pacientes SET MedicoUserId = {remote.Id} WHERE Id = {patient.Id}"));
        Assert.Equal(547, error.Number);
        // Simulate an old writer using only the pre-existing contract on the expanded schema.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Pacientes SET NomePaciente = {"Synthetic old writer"} WHERE Id = {patient.Id} AND ClinicaId = {1}");
        Assert.Equal("Synthetic old writer", (await clinicA.Pacientes.SingleAsync(x => x.Id == patient.Id)).NomePaciente);
    }

    private static Task<string> Data(AppDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT (SELECT * FROM Clinicas ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES) +
               (SELECT * FROM Users ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES) +
               (SELECT * FROM Pacientes ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES) +
               (SELECT * FROM FaturamentosMedicos ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES) +
               (SELECT * FROM IdempotencyRequests ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES) +
               (SELECT * FROM ConfiguracoesSistema ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES) AS [Value]
        """).SingleAsync();

    private static async Task<string[]> Schema(AppDbContext db) => (await db.Database.SqlQueryRaw<string>("""
        SELECT CONCAT('column|', OBJECT_NAME(c.object_id), '|', c.name, '|', TYPE_NAME(c.user_type_id), '|',
            c.max_length, '|', c.precision, '|', c.scale, '|', c.is_nullable, '|', c.is_identity, '|', d.definition) AS [Value]
        FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
        LEFT JOIN sys.default_constraints d ON d.object_id = c.default_object_id
        WHERE t.is_ms_shipped = 0
        UNION ALL
        SELECT CONCAT('index|', OBJECT_NAME(i.object_id), '|', i.name, '|', i.is_unique, '|', i.type, '|',
            i.filter_definition, '|', COL_NAME(ic.object_id, ic.column_id), '|', ic.key_ordinal, '|', ic.is_included_column)
        FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.tables t ON t.object_id = i.object_id WHERE t.is_ms_shipped = 0
        UNION ALL
        SELECT CONCAT('fk|', fk.name, '|', OBJECT_NAME(fk.parent_object_id), '|', COL_NAME(fc.parent_object_id, fc.parent_column_id),
            '|', OBJECT_NAME(fk.referenced_object_id), '|', COL_NAME(fc.referenced_object_id, fc.referenced_column_id), '|',
            fk.delete_referential_action, '|', fk.is_disabled, '|', fk.is_not_trusted)
        FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = fk.object_id
        UNION ALL
        SELECT CONCAT('check|', OBJECT_NAME(parent_object_id), '|', name, '|', definition, '|', is_disabled, '|', is_not_trusted)
        FROM sys.check_constraints
        UNION ALL
        SELECT CONCAT('fts|', OBJECT_NAME(f.object_id), '|', c.name, '|', c.is_accent_sensitivity_on, '|', f.change_tracking_state,
            '|', COALESCE(CONVERT(varchar, f.stoplist_id), 'NULL'), '|', COL_NAME(fc.object_id, fc.column_id), '|', fc.language_id)
        FROM sys.fulltext_indexes f JOIN sys.fulltext_catalogs c ON c.fulltext_catalog_id = f.fulltext_catalog_id
        JOIN sys.fulltext_index_columns fc ON fc.object_id = f.object_id
        """).ToListAsync()).Order(StringComparer.Ordinal).ToArray();

    private static async Task ExecuteScript(AppDbContext db, string script)
    {
        // Keep the connection open across GO batches, including EF's transaction boundaries.
        await db.Database.OpenConnectionAsync();
        try
        {
            foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = batch;
                command.CommandTimeout = 120;
                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            // A failing batch may leave the script's explicit transaction open.
            await db.Database.ExecuteSqlRawAsync("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            await db.Database.CloseConnectionAsync();
        }
    }
}
