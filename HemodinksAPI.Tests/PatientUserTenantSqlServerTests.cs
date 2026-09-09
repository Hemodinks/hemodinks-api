using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Data.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HemodinksAPI.Tests;

public sealed class PatientUserTenantSqlServerTests
{
    [Fact]
    public async Task SqlServer_RejectsCrossClinicPatientAndUserRelationships()
    {
        if (Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1")
        {
            Assert.Skip("Set HEMODINKS_TEST_LOCALDB=1 to run against a disposable SQL Server LocalDB database.");
        }

        // The test owns only this newly generated database; never use application configuration.
        var databaseName = $"HemodinksTenantTest_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=True;TrustServerCertificate=True")
            .Options;
        await using var context = new PlatformDbContext(options);
        try
        {
            await context.Database.EnsureCreatedAsync();
            var otherClinic = new Clinica { Nome = "Outra", Slug = "outra" };
            context.Clinicas.Add(otherClinic);
            await context.SaveChangesAsync();
            var first = CreatePatient(1);
            var second = CreatePatient(otherClinic.Id);
            var otherUser = new User
            {
                ClinicaId = otherClinic.Id, Nome = "Outro usuario", Email = "other@example.com",
                Telefone = "+5511999999999", Senha = "test-hash", PerfilId = Perfil.MedicosId
            };
            context.Users.Add(otherUser);
            context.Pacientes.AddRange(first, second);
            await context.SaveChangesAsync();
            var userFile = new UserArquivo
            {
                UserId = first.UserId, ClinicaId = 1, NomeOriginal = "test.txt",
                ContentType = "text/plain", Url = "/test.txt"
            };
            var patientFile = new PacienteArquivo
            {
                PacienteId = first.Id, ClinicaId = 1, NomeOriginal = "test.txt",
                ContentType = "text/plain", Url = "/test.txt"
            };
            context.UserArquivos.Add(userFile);
            context.PacienteArquivos.Add(patientFile);
            await context.SaveChangesAsync();

            var migration = new Schema_EnforcePatientUserClinicRelationships();
            await ExecuteOperations(context, migration.DownOperations);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET MedicoUserId = {second.UserId} WHERE Id = {first.Id}");
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteOperations(context, migration.UpOperations));
                Assert.Equal(547, error.Number);
                await transaction.RollbackAsync();
            }
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET MedicoUserId = NULL WHERE Id = {first.Id}");
            await ExecuteOperations(context, migration.UpOperations);

            // Raw SQL bypasses SaveChanges validation and exercises actual database constraints.
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET UserId = {otherUser.Id} WHERE Id = {first.Id}"));
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET MedicoUserId = {second.UserId} WHERE Id = {first.Id}"));
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET MedicoAuxiliar1UserId = {second.UserId} WHERE Id = {first.Id}"));
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET MedicoAuxiliar2UserId = {second.UserId} WHERE Id = {first.Id}"));
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE UserArquivos SET UserId = {second.UserId} WHERE Id = {userFile.Id}"));
            await AssertForeignKeyViolation(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE PacienteArquivos SET PacienteId = {second.Id} WHERE Id = {patientFile.Id}"));

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Pacientes SET MedicoUserId = {first.UserId} WHERE Id = {first.Id}");
            context.ChangeTracker.Clear();
            Assert.Equal(new DateTime(2030, 12, 1), (await context.Pacientes.SingleAsync(item => item.Id == first.Id)).DataAtendimento);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertForeignKeyViolation(Func<Task<int>> operation)
    {
        var error = await Assert.ThrowsAsync<SqlException>(operation);
        Assert.Equal(547, error.Number);
    }

    private static async Task ExecuteOperations(AppDbContext context, IReadOnlyList<MigrationOperation> operations)
    {
        var generator = context.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(operations, context.Model))
        {
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
        }
    }

    private static Paciente CreatePatient(int clinicId) => new()
    {
        ClinicaId = clinicId, NomePaciente = $"Paciente {clinicId}", DataAtendimento = new DateTime(2030, 12, 1),
        User = new User
        {
            ClinicaId = clinicId, Nome = $"Paciente {clinicId}", Email = $"patient{clinicId}@example.com",
            Telefone = "+5511999999999", Senha = "test-hash", PerfilId = Perfil.PacientesId
        }
    };
}
