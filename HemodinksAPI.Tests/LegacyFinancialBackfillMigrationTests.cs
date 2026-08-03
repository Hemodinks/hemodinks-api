using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HemodinksAPI.Tests;

public sealed class LegacyFinancialBackfillMigrationTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task Migration_backfills_valid_legacy_values_and_audits_invalid_originals()
    {
        var databaseName = $"HemodinksLegacyBackfill_{Guid.NewGuid():N}";
        var connectionString = SqlServerTestConnection.Create(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var tenant = ClinicaContextFactory.CreateDefaultResolved();

        await using var db = new AppDbContext(options, tenant);
        try
        {
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260721160019_AddPartialClinicPlanModules");

            var doctor = new User { ClinicaId = Clinica.DefaultId, Nome = "Médico legado", Telefone = "+5581999000001", Cpf = "11144477735", Email = $"doctor-{databaseName}@test.local", Senha = "hash", PerfilId = Perfil.MedicosId };
            var validUser = new User { ClinicaId = Clinica.DefaultId, Nome = "Paciente legado válido", Telefone = "+5581999000002", Cpf = "52998224725", Email = $"valid-{databaseName}@test.local", Senha = "hash", PerfilId = Perfil.PacientesId };
            var invalidUser = new User { ClinicaId = Clinica.DefaultId, Nome = "Paciente legado inválido", Telefone = "+5581999000003", Cpf = "16899535009", Email = $"invalid-{databaseName}@test.local", Senha = "hash", PerfilId = Perfil.PacientesId };
            db.Users.AddRange(doctor, validUser, invalidUser);
            await db.SaveChangesAsync();

            var validPatient = new Paciente { ClinicaId = Clinica.DefaultId, UserId = validUser.Id, NomePaciente = validUser.Nome, MedicoUserId = doctor.Id };
            var invalidPatient = new Paciente { ClinicaId = Clinica.DefaultId, UserId = invalidUser.Id, NomePaciente = invalidUser.Nome, MedicoUserId = doctor.Id };
            db.Pacientes.AddRange(validPatient, invalidPatient);
            await db.SaveChangesAsync();
            var patientIds = new[] { validPatient.Id, invalidPatient.Id };

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [Pacientes]
                SET [Pagamento] = N'R$ 1.234,56', [RepasseGlosa] = N'R$ 234,56', [StatusPago] = 1,
                    [Data] = '2026-06-15', [Procedimento] = N'Cirurgia legada válida'
                WHERE [Id] = {patientIds[0]};

                UPDATE [Pacientes]
                SET [Pagamento] = N'pago via permuta', [RepasseGlosa] = N'sem informação', [StatusPago] = 0,
                    [Data] = '2026-06-16', [Procedimento] = N'Cirurgia legada inválida'
                WHERE [Id] = {patientIds[1]};

                DELETE FROM [FaturamentosMedicos] WHERE [PacienteId] IN ({patientIds[0]}, {patientIds[1]});
                """);

            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();

            var validBilling = await db.Faturamentos.IgnoreQueryFilters()
                .Include(x => x.AtendimentoCirurgico)
                .Include(x => x.ContasReceber).ThenInclude(x => x.Recebimentos)
                .SingleAsync(x => x.AtendimentoCirurgico.PacienteId == patientIds[0] && x.Observacao!.Contains("LEG-PACIENTE-FINANCEIRO"));
            Assert.Equal(1234.56m, validBilling.ValorApresentado);
            Assert.Equal(234.56m, validBilling.ValorGlosado);
            Assert.Equal(1000m, validBilling.ValorReconhecido);
            var account = Assert.Single(validBilling.ContasReceber);
            Assert.Equal(1000m, account.ValorAjustado);
            Assert.Equal(1000m, Assert.Single(account.Recebimentos).ValorRecebido);

            var inconsistencies = await db.FinanceiroMigracaoInconsistencias.IgnoreQueryFilters()
                .Where(x => x.PacienteId == patientIds[1]).OrderBy(x => x.Campo).ToListAsync();
            Assert.Collection(inconsistencies,
                item => { Assert.Equal("Paciente.Pagamento", item.Campo); Assert.Equal("pago via permuta", item.ValorOriginal); },
                item => { Assert.Equal("Paciente.RepasseGlosa", item.Campo); Assert.Equal("sem informação", item.ValorOriginal); });
            Assert.False(await db.Faturamentos.IgnoreQueryFilters().AnyAsync(x => x.AtendimentoCirurgico.PacienteId == patientIds[1]));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
