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

public sealed class SurgicalTeamSqlServerTests
{
    [Fact]
    public async Task SurgicalTeam_SqlServer_EnforcesTeamConstraintsAndClinicForeignKeys()
    {
        RequireLocalDb();
        await using var context = CreateContext();
        try
        {
            await context.Database.EnsureCreatedAsync();
            var seed = await SeedAsync(context);
            var doctor = seed.PrincipalId;
            var assistant = seed.AssistantId;
            (int? Principal, int? First, int? Second)[] allowed =
            [
                (doctor, null, null),
                (doctor, doctor, null),
                (doctor, null, doctor),
                (null, doctor, null),
                (null, null, doctor),
                (null, doctor, assistant),
                (doctor, doctor, assistant),
                (doctor, assistant, doctor)
            ];
            foreach (var team in allowed)
            {
                Assert.Equal(1, await UpdateTeam(context, seed.AttendanceId, team.Principal, team.First, team.Second));
                var stored = await context.AtendimentosCirurgicos.AsNoTracking()
                    .SingleAsync(item => item.Id == seed.AttendanceId);
                Assert.Equal(team.Principal, stored.MedicoResponsavelId);
                Assert.Equal(team.First, stored.MedicoAuxiliar1Id);
                Assert.Equal(team.Second, stored.MedicoAuxiliar2Id);
            }

            await AssertConstraintViolation(
                () => UpdateTeam(context, seed.AttendanceId, null, null, null),
                "CK_AtendimentosCirurgicos_EquipeObrigatoria");
            await AssertConstraintViolation(
                () => UpdateTeam(context, seed.AttendanceId, doctor, assistant, assistant),
                "CK_AtendimentosCirurgicos_AuxiliaresDistintos");
            await AssertConstraintViolation(
                () => UpdateTeam(context, seed.AttendanceId, null, assistant, assistant),
                "CK_AtendimentosCirurgicos_AuxiliaresDistintos");

            await UpdateTeam(context, seed.AttendanceId, doctor, null, null);
            // Bypass SaveChanges validation: the database itself must reject doctors owned by clinic B.
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE AtendimentosCirurgicos SET MedicoResponsavelId = {seed.OtherClinicDoctorId} WHERE Id = {seed.AttendanceId}"));
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE AtendimentosCirurgicos SET MedicoAuxiliar1Id = {seed.OtherClinicDoctorId} WHERE Id = {seed.AttendanceId}"));
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE AtendimentosCirurgicos SET MedicoAuxiliar2Id = {seed.OtherClinicDoctorId} WHERE Id = {seed.AttendanceId}"));
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET MedicoUserId = {seed.OtherClinicDoctorId} WHERE Id = {seed.PatientId}"));
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET MedicoAuxiliar1UserId = {seed.OtherClinicDoctorId} WHERE Id = {seed.PatientId}"));
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET MedicoAuxiliar2UserId = {seed.OtherClinicDoctorId} WHERE Id = {seed.PatientId}"));

            var unchanged = await context.AtendimentosCirurgicos.AsNoTracking()
                .SingleAsync(item => item.Id == seed.AttendanceId);
            Assert.Equal(Clinica.DefaultId, unchanged.ClinicaId);
            Assert.Equal(doctor, unchanged.MedicoResponsavelId);
            Assert.Null(unchanged.MedicoAuxiliar1Id);
            Assert.Null(unchanged.MedicoAuxiliar2Id);
            var patient = await context.Pacientes.AsNoTracking().SingleAsync(item => item.Id == seed.PatientId);
            Assert.Equal(doctor, patient.MedicoUserId);
            Assert.Null(patient.MedicoAuxiliar1UserId);
            Assert.Null(patient.MedicoAuxiliar2UserId);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task SurgicalTeam_SqlServer_MigrationPreservesLegacyDataAndBlocksIncompatibleRollbackBeforeSchemaChanges()
    {
        RequireLocalDb();
        await using var context = CreateContext();
        try
        {
            await context.Database.EnsureCreatedAsync();
            var seed = await SeedAsync(context);
            var migration = new Schema_AllowOptionalSurgicalPrincipal();
            var originalData = await AttendanceData(context);

            // Reconstruct the preceding schema using compatible, existing attendance data.
            await ExecuteOperations(context, migration.DownOperations);
            Assert.Equal(originalData, await AttendanceData(context));
            var legacySchema = await AttendanceSchema(context);
            Assert.Contains(legacySchema, value => value == "COLUMN:MedicoResponsavelId:0");
            Assert.Contains(legacySchema, value => value.StartsWith("CHECK:CK_AtendimentosCirurgicos_MedicosDistintos:"));
            await AssertConstraintViolation(
                () => UpdateTeam(context, seed.AttendanceId, seed.PrincipalId, seed.PrincipalId, null),
                "CK_AtendimentosCirurgicos_MedicosDistintos");

            await context.Database.ExecuteSqlRawAsync(context.GetService<IHistoryRepository>().GetCreateScript());
            // Migration discovery uses the DbContext attribute on historical migrations.
            // PlatformDbContext is only for seed/constraint checks; scripts use AppDbContext.
            await using var migrationContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(context.Database.GetConnectionString()).Options, new ClinicaContext());
            var script = migrationContext.GetService<IMigrator>().GenerateScript(
                "20261012210010_AddEventAuthenticatedCreator", "20261017015429_Schema_AllowOptionalSurgicalPrincipal",
                MigrationsSqlGenerationOptions.Idempotent);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                    if (!string.IsNullOrWhiteSpace(batch)) await context.Database.ExecuteSqlRawAsync(batch);
                Assert.Equal(originalData, await AttendanceData(context));
            }
            Assert.Single(await context.Database.SqlQueryRaw<string>(
                "SELECT MigrationId AS [Value] FROM __EFMigrationsHistory").ToListAsync());
            Assert.Equal(originalData, await AttendanceData(context));
            var upgradedSchema = await AttendanceSchema(context);
            Assert.Contains(upgradedSchema, value => value == "COLUMN:MedicoResponsavelId:1");
            Assert.Contains(upgradedSchema, value => value.StartsWith("CHECK:CK_AtendimentosCirurgicos_AuxiliaresDistintos:"));
            Assert.Contains(upgradedSchema, value => value.StartsWith("CHECK:CK_AtendimentosCirurgicos_EquipeObrigatoria:"));

            (int? Principal, int? First, int? Second)[] newTeams =
            [
                (null, seed.AssistantId, null),
                (null, null, seed.AssistantId),
                (seed.PrincipalId, seed.PrincipalId, null),
                (seed.PrincipalId, null, seed.PrincipalId)
            ];
            foreach (var team in newTeams)
            {
                Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO AtendimentosCirurgicos
                        (ClinicaId, PacienteId, DataProcedimento, MedicoResponsavelId,
                         MedicoAuxiliar1Id, MedicoAuxiliar2Id, Status, DataCadastro)
                    VALUES ({Clinica.DefaultId}, {seed.PatientId}, {new DateTime(2030, 12, 2)},
                            {team.Principal}, {team.First}, {team.Second}, {"Planejado"}, {new DateTime(2030, 12, 1)})
                    """));
                var dataBeforeRollback = await AttendanceData(context);

                // No outer transaction: the migration guard must run before any schema mutation.
                var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteOperations(context, migration.DownOperations));
                Assert.True(error.Number >= 50000, $"Expected an explicit migration guard, received SQL error {error.Number}.");
                Assert.Equal(upgradedSchema, await AttendanceSchema(context));
                Assert.Equal(dataBeforeRollback, await AttendanceData(context));

                Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM AtendimentosCirurgicos WHERE Id <> {seed.AttendanceId}"));
                Assert.Equal(originalData, await AttendanceData(context));
            }

            // Once incompatible rows are removed, rollback remains usable and preserves the baseline.
            await ExecuteOperations(context, migration.DownOperations);
            Assert.Equal(legacySchema, await AttendanceSchema(context));
            Assert.Equal(originalData, await AttendanceData(context));
            await ExecuteOperations(context, migration.UpOperations);
            Assert.Equal(upgradedSchema, await AttendanceSchema(context));
            Assert.Equal(originalData, await AttendanceData(context));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static void RequireLocalDb()
    {
        if (Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1"
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING")))
            Assert.Skip("Requires isolated SQL Server; set HEMODINKS_TEST_LOCALDB=1 or HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING.");
    }

    private static PlatformDbContext CreateContext()
    {
        // Only this test-generated database is created/deleted; no application configuration is read.
        var databaseName = $"HemodinksSurgicalTeamTest_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlServerTestConnection.Create(databaseName))
            .Options;
        return new PlatformDbContext(options);
    }

    private static async Task<Seed> SeedAsync(AppDbContext context)
    {
        var otherClinic = new Clinica { Nome = "Synthetic clinic B", Slug = "surgical-team-clinic-b" };
        var principal = CreateDoctor(Clinica.DefaultId, "principal");
        var assistant = CreateDoctor(Clinica.DefaultId, "assistant");
        context.Clinicas.Add(otherClinic);
        context.Users.AddRange(principal, assistant);
        await context.SaveChangesAsync();
        var otherDoctor = CreateDoctor(otherClinic.Id, "other-clinic");
        var patient = new Paciente
        {
            ClinicaId = Clinica.DefaultId, NomePaciente = "Synthetic surgical patient",
            DataAtendimento = new DateTime(2030, 12, 1), MedicoUserId = principal.Id,
            User = new User
            {
                ClinicaId = Clinica.DefaultId, Nome = "Synthetic patient", Email = "surgical-patient@example.com",
                Telefone = "+5511999999999", Senha = "test-hash", PerfilId = Perfil.PacientesId
            }
        };
        context.Users.Add(otherDoctor);
        context.Pacientes.Add(patient);
        await context.SaveChangesAsync();
        var attendance = new AtendimentoCirurgico
        {
            ClinicaId = Clinica.DefaultId, PacienteId = patient.Id,
            DataProcedimento = new DateTime(2030, 12, 1), MedicoResponsavelId = principal.Id,
            MedicoAuxiliar1Id = assistant.Id, Diagnostico = "Synthetic baseline",
            DataCadastro = new DateTime(2030, 11, 1)
        };
        context.AtendimentosCirurgicos.Add(attendance);
        await context.SaveChangesAsync();
        return new Seed(patient.Id, attendance.Id, principal.Id, assistant.Id, otherDoctor.Id);
    }

    private static User CreateDoctor(int clinicId, string name) => new()
    {
        ClinicaId = clinicId, Nome = $"Synthetic {name}", Email = $"surgical-{name}@example.com",
        Telefone = "+5511999999999", Senha = "test-hash", PerfilId = Perfil.MedicosId
    };

    private static Task<int> UpdateTeam(AppDbContext context, int attendanceId, int? principal, int? first, int? second) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE AtendimentosCirurgicos
            SET MedicoResponsavelId = {principal}, MedicoAuxiliar1Id = {first}, MedicoAuxiliar2Id = {second}
            WHERE Id = {attendanceId}
            """);

    private static async Task AssertConstraintViolation(Func<Task<int>> operation, string constraint)
    {
        var error = await Assert.ThrowsAsync<SqlException>(operation);
        Assert.Equal(547, error.Number);
        Assert.Contains(constraint, error.Message);
    }

    private static async Task AssertForeignKeyViolation(Func<Task<int>> operation)
    {
        var error = await Assert.ThrowsAsync<SqlException>(operation);
        Assert.Equal(547, error.Number);
        Assert.Contains("FOREIGN KEY", error.Message);
    }

    private static async Task<string> AttendanceData(AppDbContext context) => Assert.Single(
        await context.Database.SqlQueryRaw<string>("""
            SELECT (SELECT * FROM AtendimentosCirurgicos ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES) AS [Value]
            """).ToListAsync());

    private static Task<List<string>> AttendanceSchema(AppDbContext context) =>
        context.Database.SqlQueryRaw<string>("""
            SELECT CONCAT('COLUMN:', name, ':', is_nullable) AS [Value]
            FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosCirurgicos')
            UNION ALL
            SELECT CONCAT('CHECK:', name, ':', definition, ':', is_disabled, ':', is_not_trusted)
            FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('AtendimentosCirurgicos')
            UNION ALL
            SELECT CONCAT('FK:', name, ':', is_disabled, ':', is_not_trusted)
            FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('AtendimentosCirurgicos')
            UNION ALL
            SELECT CONCAT('INDEX:', name, ':', is_disabled)
            FROM sys.indexes WHERE object_id = OBJECT_ID('AtendimentosCirurgicos')
            ORDER BY [Value]
            """).ToListAsync();

    private static async Task ExecuteOperations(AppDbContext context, IReadOnlyList<MigrationOperation> operations)
    {
        var generator = context.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(operations, context.GetService<IDesignTimeModel>().Model))
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
    }

    private sealed record Seed(int PatientId, int AttendanceId, int PrincipalId, int AssistantId, int OtherClinicDoctorId);
}
