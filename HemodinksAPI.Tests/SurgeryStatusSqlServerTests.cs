using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Data.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HemodinksAPI.Tests;

public sealed class SurgeryStatusSqlServerTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SurgeryStatus_MigrationBackfillsFromConsolidationDateAndAllowsOldWriters()
    {
        SqlServerTestConnection.RequireConfigured();
        var name = $"HemodinksSurgeryStatusTest_{Guid.NewGuid():N}";
        await using var db = new PlatformDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlServerTestConnection.Create(name)).Options);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var consolidated = NewPatient("consolidated", new DateTime(2026, 10, 1));
            var pending = NewPatient("pending", null);
            db.Pacientes.AddRange(consolidated, pending);
            await db.SaveChangesAsync();
            var baselineData = await ExistingData(db);
            var baselineSchema = await Schema(db);
            var migration = new Schema_AddSurgeryConsolidationStatus();

            // Rollback removes only the derived column; the consolidation date is preserved.
            await ExecuteOperations(db, migration.DownOperations);
            Assert.Equal(baselineData, await ExistingData(db));
            Assert.DoesNotContain(await Schema(db), item => item.Contains("StatusCirurgia", StringComparison.Ordinal));

            await ExecuteOperations(db, migration.UpOperations);
            Assert.Equal(baselineData, await ExistingData(db));
            Assert.Equal(baselineSchema, await Schema(db));
            var statuses = await db.Pacientes.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.StatusCirurgia);
            Assert.Equal(StatusCirurgia.Consolidada, statuses[consolidated.Id]);
            Assert.Equal(StatusCirurgia.NaoConsolidada, statuses[pending.Id]);

            // An old application does not know the new column and must still be able to insert.
            var oldWriter = NewPatient("old-writer", null).User;
            db.Users.Add(oldWriter);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Pacientes (ClinicaId, UserId, NomePaciente) VALUES ({Clinica.DefaultId}, {oldWriter.Id}, {"Synthetic old writer"});
                """);
            Assert.Equal(StatusCirurgia.NaoConsolidada,
                (await db.Pacientes.AsNoTracking().SingleAsync(p => p.UserId == oldWriter.Id)).StatusCirurgia);

            var invalid = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET StatusCirurgia = {"Invalido"} WHERE Id = {pending.Id}"));
            Assert.Equal(547, invalid.Number);
            Assert.Contains("CK_Pacientes_StatusCirurgia", invalid.Message);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static Paciente NewPatient(string name, DateTime? consolidatedAt) => new()
    {
        NomePaciente = $"Synthetic {name}", DataAtendimento = consolidatedAt,
        User = new User
        {
            Nome = $"Synthetic {name}", Email = $"surgery-status-{name}@example.com", Telefone = "+5511999999999",
            Senha = "test-hash", PerfilId = Perfil.PacientesId
        }
    };

    private static async Task<string> ExistingData(AppDbContext db) => Assert.Single(
        await db.Database.SqlQueryRaw<string>("""
            SELECT (SELECT Id, ClinicaId, UserId, NomePaciente, Data, DataAtendimento, TipoCirurgia, StatusPago
                    FROM Pacientes ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES) AS [Value]
            """).ToListAsync());

    private static Task<List<string>> Schema(AppDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT CONCAT('COLUMN:', name, ':', TYPE_NAME(user_type_id), ':', max_length, ':', is_nullable) AS [Value]
        FROM sys.columns WHERE object_id = OBJECT_ID('Pacientes')
        UNION ALL SELECT CONCAT('CHECK:', name, ':', definition, ':', is_disabled, ':', is_not_trusted)
        FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('Pacientes')
        UNION ALL SELECT CONCAT('DEFAULT:', COL_NAME(parent_object_id, parent_column_id), ':', definition)
        FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('Pacientes')
        UNION ALL SELECT CONCAT('INDEX:', name, ':', is_disabled)
        FROM sys.indexes WHERE object_id = OBJECT_ID('Pacientes')
        ORDER BY [Value]
        """).ToListAsync();

    private static async Task ExecuteOperations(AppDbContext db, IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.GetService<IDesignTimeModel>().Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
    }
}
