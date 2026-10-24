using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HemodinksAPI.Tests;

public sealed class RestoreDatabaseIntegrityTests
{
    [Fact]
    public void CheckerManifest_CoversCurrentForeignKeysAndChecks()
    {
        using var db = CreateContext("restore184-model-only");
        var model = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var sql = ReadScript();
        var missing = model.Tables.SelectMany(table => table.ForeignKeyConstraints)
            .Select(key => $"(N'{key.Name}', N'{key.Table.Name}', N'{key.PrincipalTable.Name}', N'{string.Join(",", key.Columns.Select(column => column.Name))}', N'{string.Join(",", key.PrincipalColumns.Select(column => column.Name))}')")
            .Where(manifest => !sql.Contains(manifest, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "Missing current FK manifest entries:\n" + string.Join(",\n", missing));
        foreach (var check in model.Tables.SelectMany(table => table.CheckConstraints))
            Assert.Contains($"N'{check.Name}'", sql);
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task Checker_RejectsUnexpectedDatabaseAndMissingOrWrongConfirmation()
    {
        var script = ReadScript();
        SqlServerTestConnection.RequireConfigured();
        foreach (var invalidPrefix in new[] { false, true })
        {
            var name = $"{(invalidPrefix ? "restore184wrong-" : "restore184-")}{Guid.NewGuid():N}";
            await WithDatabase(name, async connection =>
            {
                foreach (var confirmation in new string?[] { null, "restore184-another-copy", name + " ", name.ToUpperInvariant(), invalidPrefix ? name : null })
                {
                    var result = await RunChecker(connection, script, confirmation);
                    Assert.Equal(51840, result.Error?.Number);
                    Assert.Empty(result.Counts);
                }
            });
        }
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task Checker_RejectsMissingSchemaWithGenericError()
    {
        var script = ReadScript();
        SqlServerTestConnection.RequireConfigured();
        var name = $"restore184-{Guid.NewGuid():N}";
        await WithDatabase(name, async connection =>
        {
            var result = await RunChecker(connection, script, name);
            Assert.Equal(51841, result.Error?.Number);
            Assert.DoesNotContain(name, result.Error!.Message);
            await using var permissions = connection.CreateCommand();
            permissions.CommandText = "CREATE USER Restore184Reader WITHOUT LOGIN; GRANT SELECT TO Restore184Reader; EXECUTE AS USER = N'Restore184Reader';";
            await permissions.ExecuteNonQueryAsync();
            try
            {
                var hiddenMetadata = await RunChecker(connection, script, name);
                Assert.Equal(51841, hiddenMetadata.Error?.Number);
                Assert.Contains("permissions required", hiddenMetadata.Error!.Message);
                Assert.Empty(hiddenMetadata.Counts);
            }
            finally
            {
                permissions.CommandText = "REVERT";
                await permissions.ExecuteNonQueryAsync();
            }
        });
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task Checker_CurrentSchemaAcceptsSyntheticDataAndRejectsCorruptionWithoutChangingIt()
    {
        var script = ReadScript();
        SqlServerTestConnection.RequireConfigured();
        var name = $"restore184-{Guid.NewGuid():N}";
        await using var db = CreateContext(name);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var patient = new Paciente
            {
                ClinicaId = 1, NomePaciente = "Synthetic recovery patient",
                User = NewUser(1, "patient", Perfil.PacientesId)
            };
            var remoteClinic = new Clinica { Nome = "Synthetic clinic B", Slug = "restore184-b" };
            db.Pacientes.Add(patient);
            db.Clinicas.Add(remoteClinic);
            await db.SaveChangesAsync();
            var remote = NewUser(remoteClinic.Id, "remote", Perfil.MedicosId);
            var identity = new UsuarioGlobal { Nome = "Synthetic identity", Email = "restore184@example.com", Senha = "synthetic-hash" };
            db.Users.Add(remote);
            db.UsuariosGlobais.Add(identity);
            await db.SaveChangesAsync();
            var membership = new UsuarioClinica { UsuarioGlobalId = identity.Id, ClinicaId = 1, UserId = patient.UserId, PerfilId = Perfil.PacientesId };
            db.UsuariosClinicas.Add(membership);
            await db.SaveChangesAsync();
            // EnsureCreated has no migration history. Supply only the explicit current-schema
            // marker for this fixture; operational copies must retain their real history.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE dbo.__EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
                INSERT dbo.__EFMigrationsHistory VALUES (N'20261019121529_Schema_AddSurgeryType', N'10.0.0');
                """);
            await db.Database.OpenConnectionAsync();
            var connection = (SqlConnection)db.Database.GetDbConnection();
            var baseline = await RunChecker(connection, script, name);
            Assert.Null(baseline.Error);
            Assert.NotEmpty(baseline.Counts);
            Assert.All(baseline.Counts.Values, count => Assert.Equal(0, count));
            await db.Database.ExecuteSqlRawAsync("CREATE USER Restore184Verifier WITHOUT LOGIN; GRANT SELECT, VIEW DEFINITION TO Restore184Verifier; EXECUTE AS USER = N'Restore184Verifier'");
            try
            {
                var readOnly = await RunChecker(connection, script, name);
                Assert.Null(readOnly.Error);
                Assert.Equal(baseline.Counts.Count, readOnly.Counts.Count);
                Assert.All(readOnly.Counts.Values, count => Assert.Equal(0, count));
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("REVERT");
            }

            await db.Database.ExecuteSqlRawAsync("UPDATE dbo.__EFMigrationsHistory SET MigrationId = N'20990101000000_UnknownFutureSchema'");
            Assert.Equal(51841, (await RunChecker(connection, script, name)).Error?.Number);
            await db.Database.ExecuteSqlRawAsync("UPDATE dbo.__EFMigrationsHistory SET MigrationId = N'20261019121529_Schema_AddSurgeryType'");

            await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Pacientes DROP CONSTRAINT CK_Pacientes_TipoCirurgia");
            Assert.Equal(51841, (await RunChecker(connection, script, name)).Error?.Number);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Pacientes WITH CHECK ADD CONSTRAINT CK_Pacientes_TipoCirurgia CHECK (TipoCirurgia IN (N'Eletiva', N'Urgencia'))");

            // Name-only matches must not satisfy the manifest: dropping a FK fails closed.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Pacientes DROP CONSTRAINT FK_Pacientes_Users_ClinicaId_MedicoUserId");
            var missing = await RunChecker(connection, script, name);
            Assert.Equal(51841, missing.Error?.Number);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Pacientes WITH CHECK ADD CONSTRAINT FK_Pacientes_Users_ClinicaId_MedicoUserId FOREIGN KEY (MedicoUserId) REFERENCES dbo.Users (Id)");
            Assert.Equal(51841, (await RunChecker(connection, script, name)).Error?.Number);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Pacientes DROP CONSTRAINT FK_Pacientes_Users_ClinicaId_MedicoUserId");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Pacientes WITH CHECK ADD CONSTRAINT FK_Pacientes_Users_ClinicaId_MedicoUserId FOREIGN KEY (ClinicaId,MedicoUserId) REFERENCES dbo.Users (ClinicaId,Id)");

            await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Pacientes NOCHECK CONSTRAINT FK_Pacientes_Users_ClinicaId_MedicoUserId");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Pacientes SET MedicoUserId = {remote.Id} WHERE Id = {patient.Id}");
            var mismatch = await RunChecker(connection, script, name);
            Assert.Equal(51842, mismatch.Error?.Number);
            Assert.Equal(1, mismatch.Counts["ForeignKey:FK_Pacientes_Users_ClinicaId_MedicoUserId"]);
            Assert.True(mismatch.Counts["DisabledOrUntrustedConstraints"] > 0);
            Assert.Equal(remote.Id, await db.Pacientes.AsNoTracking().Where(item => item.Id == patient.Id).Select(item => item.MedicoUserId).SingleAsync());

            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Pacientes SET MedicoUserId = {int.MaxValue} WHERE Id = {patient.Id}");
            var orphan = await RunChecker(connection, script, name);
            Assert.Equal(51842, orphan.Error?.Number);
            Assert.Equal(1, orphan.Counts["ForeignKey:FK_Pacientes_Users_ClinicaId_MedicoUserId"]);
            await db.Database.ExecuteSqlRawAsync("UPDATE dbo.Pacientes SET MedicoUserId = NULL; ALTER TABLE dbo.Pacientes CHECK CONSTRAINT FK_Pacientes_Users_ClinicaId_MedicoUserId");
            var untrusted = await RunChecker(connection, script, name);
            Assert.Equal(51842, untrusted.Error?.Number);
            Assert.True(untrusted.Counts["DisabledOrUntrustedConstraints"] > 0);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Pacientes WITH CHECK CHECK CONSTRAINT FK_Pacientes_Users_ClinicaId_MedicoUserId");

            // Both single-column FKs remain valid, but the membership crosses clinics.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.UsuariosClinicas SET ClinicaId = {remoteClinic.Id} WHERE Id = {membership.Id}");
            var membershipMismatch = await RunChecker(connection, script, name);
            Assert.Equal(51842, membershipMismatch.Error?.Number);
            Assert.Equal(1, membershipMismatch.Counts["Tenant:FK_UsuariosClinicas_Users_UserId"]);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.UsuariosClinicas SET ClinicaId = 1 WHERE Id = {membership.Id}");

            var otherIdentity = new UsuarioGlobal { Nome = "Synthetic second identity", Email = "restore184-other@example.com", Senha = "synthetic-hash" };
            db.UsuariosGlobais.Add(otherIdentity);
            await db.SaveChangesAsync();
            db.AuthenticationSessions.Add(new AuthenticationSession
            {
                Id = Guid.NewGuid(), UsuarioGlobalId = otherIdentity.Id, UsuarioClinicaId = membership.Id,
                RefreshTokenHash = new string('0', 64)
            });
            await db.SaveChangesAsync();
            var sessionMismatch = await RunChecker(connection, script, name);
            Assert.Equal(51842, sessionMismatch.Error?.Number);
            Assert.Equal(1, sessionMismatch.Counts["SessionIdentityMismatch"]);
            Assert.DoesNotContain("@example.com", sessionMismatch.Error!.Message);
            await db.Database.ExecuteSqlRawAsync("DELETE dbo.AuthenticationSessions");
            Assert.Null((await RunChecker(connection, script, name)).Error);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static User NewUser(int clinic, string name, int profile) => new()
    {
        ClinicaId = clinic, Nome = $"Synthetic {name}", Email = $"restore184-{name}@example.com",
        Telefone = "+5511999999999", Senha = "synthetic-hash", PerfilId = profile
    };

    private static PlatformDbContext CreateContext(string name) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(SqlServerTestConnection.Create(name)).Options);

    private static string ReadScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, "scripts", "ValidateRestoredDatabase.sql"));
    }

    private static async Task WithDatabase(string name, Func<SqlConnection, Task> action)
    {
        // Only a freshly generated local/test database is created or dropped by the fixture.
        await using var master = new SqlConnection(SqlServerTestConnection.Create("master"));
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
        try
        {
            await using var connection = new SqlConnection(SqlServerTestConnection.Create(name));
            await connection.OpenAsync();
            await action(connection);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            command.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<CheckResult> RunChecker(SqlConnection connection, string script, string? confirmation)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = "EXEC sys.sp_set_session_context @key=N'Restore184ExpectedDatabase', @value=@expected";
        command.Parameters.AddWithValue("@expected", (object?)confirmation ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        command.Parameters.Clear();
        command.CommandText = script;
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                while (await reader.ReadAsync())
                    counts.Add(reader.GetString(0), reader.GetInt64(1));
            } while (await reader.NextResultAsync());
            return new(counts, null);
        }
        catch (SqlException error)
        {
            return new(counts, error);
        }
    }

    private sealed record CheckResult(Dictionary<string, long> Counts, SqlException? Error);
}
