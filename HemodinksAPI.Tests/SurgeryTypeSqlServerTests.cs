using System.Reflection;
using System.Text.RegularExpressions;
using HemodinksAPI.Application.Tenancy;
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

public sealed class SurgeryTypeSqlServerTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SurgeryType_MigrationBackfillsLegacyRowsAndAllowsOldWritersWithoutChangingExistingData()
    {
        RequireLocalDb();
        await using var db = CreateContext();
        try
        {
            await db.Database.EnsureCreatedAsync();
            var seed = await SeedAsync(db);
            var migration = new Schema_AddSurgeryType();
            var baseline = await ExistingData(db);
            var baselineSchema = await Schema(db);
            await ExecuteOperations(db, migration.DownOperations);
            Assert.Equal(baseline, await ExistingData(db));
            Assert.DoesNotContain(await Schema(db), item => item.Contains("TipoCirurgia", StringComparison.Ordinal));

            await db.Database.ExecuteSqlRawAsync(db.GetService<IHistoryRepository>().GetCreateScript());
            await using var scriptContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(db.Database.GetConnectionString()).Options, new ClinicaContext());
            var migrationId = migration.GetType().GetCustomAttribute<MigrationAttribute>()!.Id;
            var script = scriptContext.GetService<IMigrator>().GenerateScript(
                "20261017015429_Schema_AllowOptionalSurgicalPrincipal", migrationId,
                MigrationsSqlGenerationOptions.Idempotent);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                    if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
                Assert.Equal(baseline, await ExistingData(db));
                Assert.Equal(baselineSchema, await Schema(db));
                Assert.All(await db.Pacientes.AsNoTracking().ToListAsync(), item => Assert.Equal(TipoCirurgia.Eletiva, item.TipoCirurgia));
                Assert.All(await db.AtendimentosCirurgicos.AsNoTracking().ToListAsync(), item => Assert.Equal(TipoCirurgia.Eletiva, item.TipoCirurgia));
            }
            Assert.Single(await db.Database.SqlQueryRaw<string>("SELECT MigrationId AS [Value] FROM __EFMigrationsHistory").ToListAsync());

            // An old application does not know the new column and must still be able to insert.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Pacientes (ClinicaId, UserId, NomePaciente, MedicoUserId)
                VALUES ({Clinica.DefaultId}, {seed.OldWriterUserId}, {"Synthetic old writer"}, {seed.DoctorId});
                INSERT INTO AtendimentosCirurgicos
                    (ClinicaId, PacienteId, DataProcedimento, MedicoAuxiliar1Id, Status)
                VALUES ({Clinica.DefaultId}, {seed.PatientId}, {new DateTime(2032, 1, 2)}, {seed.DoctorId}, {"Planejado"});
                """);
            Assert.Equal(2, await db.Pacientes.CountAsync(item => item.TipoCirurgia == TipoCirurgia.Eletiva));
            Assert.Equal(2, await db.AtendimentosCirurgicos.CountAsync(item => item.TipoCirurgia == TipoCirurgia.Eletiva));

            foreach (var table in new[] { "Pacientes", "AtendimentosCirurgicos" })
            {
                var invalidSql = table == "Pacientes"
                    ? "UPDATE [Pacientes] SET [TipoCirurgia] = N'Invalida'"
                    : "UPDATE [AtendimentosCirurgicos] SET [TipoCirurgia] = N'Invalida'";
                var missingSql = table == "Pacientes"
                    ? "UPDATE [Pacientes] SET [TipoCirurgia] = NULL"
                    : "UPDATE [AtendimentosCirurgicos] SET [TipoCirurgia] = NULL";
                var invalid = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                    invalidSql));
                Assert.Equal(547, invalid.Number);
                Assert.Contains($"CK_{table}_TipoCirurgia", invalid.Message);
                var missing = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                    missingSql));
                Assert.Equal(515, missing.Number);
            }

            // The additive change must retain the composite tenant foreign keys and #179 rules.
            await AssertForeignKeyViolation(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET MedicoUserId = {seed.OtherClinicDoctorId} WHERE Id = {seed.PatientId}"));
            await AssertForeignKeyViolation(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE AtendimentosCirurgicos SET MedicoAuxiliar1Id = {seed.OtherClinicDoctorId} WHERE Id = {seed.AttendanceId}"));
            var emptyTeam = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE AtendimentosCirurgicos SET MedicoResponsavelId = NULL, MedicoAuxiliar1Id = NULL, MedicoAuxiliar2Id = NULL WHERE Id = {seed.AttendanceId}"));
            Assert.Equal(547, emptyTeam.Number);
            Assert.Contains("CK_AtendimentosCirurgicos_EquipeObrigatoria", emptyTeam.Message);
            var defaultOnlyData = await ExistingData(db);
            var expandedSchema = await Schema(db);
            await ExecuteOperations(db, migration.DownOperations);
            Assert.Equal(defaultOnlyData, await ExistingData(db));
            await ExecuteOperations(db, migration.UpOperations);
            Assert.Equal(defaultOnlyData, await ExistingData(db));
            Assert.Equal(expandedSchema, await Schema(db));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SurgeryType_RollbackRefusesUrgencyInEitherTableBeforeChangingSchemaOrData()
    {
        RequireLocalDb();
        await using var db = CreateContext();
        try
        {
            await db.Database.EnsureCreatedAsync();
            var seed = await SeedAsync(db);
            var migration = new Schema_AddSurgeryType();
            var originalData = await ExistingData(db);
            var originalSchema = await Schema(db);

            foreach (var patientUrgent in new[] { true, false })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Pacientes SET TipoCirurgia = {(patientUrgent ? "Urgencia" : "Eletiva")} WHERE Id = {seed.PatientId};
                    UPDATE AtendimentosCirurgicos SET TipoCirurgia = {(patientUrgent ? "Eletiva" : "Urgencia")} WHERE Id = {seed.AttendanceId};
                    """);
                Assert.Equal(patientUrgent ? TipoCirurgia.Urgencia : TipoCirurgia.Eletiva,
                    (await db.Pacientes.AsNoTracking().SingleAsync(item => item.Id == seed.PatientId)).TipoCirurgia);
                Assert.Equal(patientUrgent ? TipoCirurgia.Eletiva : TipoCirurgia.Urgencia,
                    (await db.AtendimentosCirurgicos.AsNoTracking().SingleAsync(item => item.Id == seed.AttendanceId)).TipoCirurgia);
                var before = await SurgeryTypes(db);

                // Intentionally no outer transaction: preflight must precede every schema mutation.
                var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteOperations(db, migration.DownOperations));
                Assert.True(error.Number >= 50000);
                Assert.Contains("Rollback bloqueado", error.Message);
                Assert.Equal(originalData, await ExistingData(db));
                Assert.Equal(originalSchema, await Schema(db));
                Assert.Equal(before, await SurgeryTypes(db));

                // Simulate the old binary updating unrelated data with the expanded schema retained.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Pacientes SET NomePaciente = NomePaciente WHERE Id = {seed.PatientId};
                    UPDATE AtendimentosCirurgicos SET Diagnostico = Diagnostico WHERE Id = {seed.AttendanceId};
                    """);
                Assert.Equal(before, await SurgeryTypes(db));
            }
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static void RequireLocalDb()
    {
        if (Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1")
            Assert.Skip("Set HEMODINKS_TEST_LOCALDB=1 to run surgery type tests in disposable LocalDB databases.");
    }

    private static PlatformDbContext CreateContext()
    {
        // Never read an application's connection string or connect to an externally configured server.
        var name = $"HemodinksSurgeryTypeTest_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
            $"Server=(localdb)\\MSSQLLocalDB;Database={name};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True").Options;
        return new PlatformDbContext(options);
    }

    private static async Task<Seed> SeedAsync(AppDbContext db)
    {
        var otherClinic = new Clinica { Nome = "Synthetic clinic B", Slug = "surgery-type-clinic-b" };
        var doctor = NewUser("doctor", Perfil.MedicosId);
        var oldWriter = NewUser("old-writer", Perfil.PacientesId);
        db.Clinicas.Add(otherClinic);
        db.Users.AddRange(doctor, oldWriter);
        await db.SaveChangesAsync();
        var otherDoctor = NewUser("other-doctor", Perfil.MedicosId);
        otherDoctor.ClinicaId = otherClinic.Id;
        var patient = new Paciente
        {
            NomePaciente = "Synthetic legacy patient", User = NewUser("patient", Perfil.PacientesId),
            Data = new DateTime(2032, 1, 1), MedicoUserId = doctor.Id,
            Medico = doctor.Nome, Pagamento = "1200,00", RepasseGlosa = "100,00",
            FaturamentoMedico = new FaturamentoMedico { HonorariosCirurgiao = 1200, ValorGlosa = 100 }
        };
        db.Users.Add(otherDoctor);
        db.Pacientes.Add(patient);
        await db.SaveChangesAsync();
        var attendance = new AtendimentoCirurgico
        {
            PacienteId = patient.Id, DataProcedimento = new DateTime(2032, 1, 1),
            MedicoResponsavelId = null, MedicoAuxiliar1Id = doctor.Id,
            Diagnostico = "Synthetic legacy diagnosis", ValorGlosa = 100, MotivoGlosa = "Synthetic reason"
        };
        attendance.Faturamentos.Add(new Faturamento
        {
            Competencia = new DateTime(2032, 1, 1), ValorApresentado = 1200,
            ValorGlosado = 100, ValorReconhecido = 1100
        });
        db.AtendimentosCirurgicos.Add(attendance);
        await db.SaveChangesAsync();
        return new Seed(patient.Id, attendance.Id, doctor.Id, otherDoctor.Id, oldWriter.Id);
    }

    private static User NewUser(string name, int profile) => new()
    {
        Nome = $"Synthetic {name}", Email = $"surgery-type-{name}@example.com", Telefone = "+5511999999999",
        Senha = "test-hash", PerfilId = profile
    };

    private static async Task<string> ExistingData(AppDbContext db) => Assert.Single(
        await db.Database.SqlQueryRaw<string>("""
            SELECT CONCAT(
                (SELECT Id, ClinicaId, UserId, NomePaciente, Data, MedicoUserId, MedicoAuxiliar1UserId,
                        MedicoAuxiliar2UserId, Pagamento, RepasseGlosa, StatusPago
                 FROM Pacientes ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES),
                (SELECT Id, ClinicaId, PacienteId, DataProcedimento, MedicoResponsavelId,
                        MedicoAuxiliar1Id, MedicoAuxiliar2Id, Diagnostico, ValorGlosa, MotivoGlosa, Status
                 FROM AtendimentosCirurgicos ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES),
                (SELECT * FROM FaturamentosMedicos ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES),
                (SELECT * FROM Faturamentos ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES)) AS [Value]
            """).ToListAsync());

    private static Task<List<string>> SurgeryTypes(AppDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT CONCAT('Paciente:', Id, ':', TipoCirurgia) AS [Value] FROM Pacientes
        UNION ALL SELECT CONCAT('Atendimento:', Id, ':', TipoCirurgia) FROM AtendimentosCirurgicos
        ORDER BY [Value]
        """).ToListAsync();

    private static Task<List<string>> Schema(AppDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT CONCAT('COLUMN:', OBJECT_NAME(object_id), ':', name, ':', TYPE_NAME(user_type_id), ':', max_length, ':', is_nullable) AS [Value]
        FROM sys.columns WHERE object_id IN (OBJECT_ID('Pacientes'), OBJECT_ID('AtendimentosCirurgicos'))
        UNION ALL SELECT CONCAT('CHECK:', name, ':', definition, ':', is_disabled, ':', is_not_trusted)
        FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('Pacientes'), OBJECT_ID('AtendimentosCirurgicos'))
        UNION ALL SELECT CONCAT('DEFAULT:', OBJECT_NAME(parent_object_id), ':', COL_NAME(parent_object_id, parent_column_id), ':', definition)
        FROM sys.default_constraints WHERE parent_object_id IN (OBJECT_ID('Pacientes'), OBJECT_ID('AtendimentosCirurgicos'))
        UNION ALL SELECT CONCAT('FK:', name, ':', is_disabled, ':', is_not_trusted)
        FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('Pacientes'), OBJECT_ID('AtendimentosCirurgicos'))
        UNION ALL SELECT CONCAT('INDEX:', OBJECT_NAME(object_id), ':', name, ':', is_disabled)
        FROM sys.indexes WHERE object_id IN (OBJECT_ID('Pacientes'), OBJECT_ID('AtendimentosCirurgicos'))
        ORDER BY [Value]
        """).ToListAsync();

    private static async Task ExecuteOperations(AppDbContext db, IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.GetService<IDesignTimeModel>().Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
    }

    private static async Task AssertForeignKeyViolation(Func<Task<int>> operation)
    {
        var error = await Assert.ThrowsAsync<SqlException>(operation);
        Assert.Equal(547, error.Number);
        Assert.Contains("FOREIGN KEY", error.Message);
    }

    private sealed record Seed(int PatientId, int AttendanceId, int DoctorId, int OtherClinicDoctorId, int OldWriterUserId);
}
