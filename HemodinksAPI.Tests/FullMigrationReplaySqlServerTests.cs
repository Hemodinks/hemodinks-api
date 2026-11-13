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

    // Bancos existentes já têm o MigrationId da baseline (#231) aplicado; o script deve só completar o restante.
    [Theory]
    [InlineData("20260908224929_AddTemporaryAccessCredentials")]
    [InlineData("20261012210010_AddEventAuthenticatedCreator")]
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

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(SqlServerTestConnection.Create($"HemodinksFullReplay_{Guid.NewGuid():N}"),
            sql => sql.CommandTimeout(120)).Options, ClinicaContextFactory.CreatePlatform());

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
