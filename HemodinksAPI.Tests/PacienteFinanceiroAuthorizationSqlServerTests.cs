using HemodinksAPI.Application.Features.Financeiro;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class PacienteFinanceiroAuthorizationSqlServerTests
{
    [Theory]
    [Trait("Category", "SqlServer")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TeamSummary_SqlServer_EnforcesTeamMembershipAndTenant_WithoutChangingFinancialContract(bool normalized)
    {
        SqlServerTestConnection.RequireConfigured();
        // Only this disposable database is created/deleted; application configuration is never used.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlServerTestConnection.Create($"HemodinksFinanceScopeTest_{Guid.NewGuid():N}")).Options;
        await using var setup = new PlatformDbContext(options);
        try
        {
            await setup.Database.EnsureCreatedAsync();
            var otherClinic = new Clinica { Nome = "Synthetic B", Slug = "finance-scope-b" };
            var team = Team(1, "a");
            var otherTeam = Team(1, "other");
            setup.AddRange(otherClinic, team, otherTeam);
            await setup.SaveChangesAsync();
            var foreignTeam = Team(otherClinic.Id, "b");
            setup.Add(foreignTeam);
            await setup.SaveChangesAsync();
            var doctorId = team.Membros.Single().UserId;
            var patients = Enumerable.Range(0, 3).Select(slot => Patient(1, $"slot-{slot}")).ToArray();
            patients[0].MedicoUserId = doctorId;
            patients[1].MedicoAuxiliar1UserId = doctorId;
            patients[2].MedicoAuxiliar2UserId = doctorId;
            var denied = Patient(1, "outside-team");
            denied.MedicoUserId = otherTeam.Membros.Single().UserId;
            var foreignPatient = Patient(otherClinic.Id, "foreign");
            foreignPatient.MedicoUserId = foreignTeam.Membros.Single().UserId;
            setup.Pacientes.AddRange(patients.Append(denied).Append(foreignPatient));
            await setup.SaveChangesAsync();
            // Attendances also exercise the existing physician rule, independently of team patient scope.
            foreach (var patient in patients.Append(denied).Append(foreignPatient))
            {
                var attendance = new AtendimentoCirurgico
                {
                    ClinicaId = patient.ClinicaId, PacienteId = patient.Id,
                    MedicoResponsavelId = patient.ClinicaId == 1 ? doctorId : foreignTeam.Membros.Single().UserId,
                    DataProcedimento = new DateTime(2030, 1, 1)
                };
                setup.Add(attendance);
                if (normalized)
                {
                    var billing = new Faturamento
                    {
                        ClinicaId = patient.ClinicaId, AtendimentoCirurgico = attendance,
                        Competencia = new DateTime(2030, 1, 1), ValorApresentado = 150m,
                        ValorGlosado = 20m, ValorReconhecido = 130m
                    };
                    setup.Add(new ContaReceber
                    {
                        ClinicaId = patient.ClinicaId, PacienteId = patient.Id, Faturamento = billing,
                        NumeroDocumento = $"SYN-{patient.Id}", Descricao = "Synthetic receivable",
                        Competencia = billing.Competencia, DataEmissao = billing.Competencia,
                        DataVencimento = billing.Competencia.AddMonths(1), ValorOriginal = 150m,
                        ValorAjustado = 130m, ValorRecebido = 50m, SaldoAberto = 80m,
                        Status = ContaReceberStatus.ParcialmenteRecebido
                    });
                }
            }
            await setup.SaveChangesAsync();
            var tenant = new ClinicaContext();
            tenant.SetCurrent(1, Clinica.DefaultSlug);
            await using var db = new AppDbContext(options, tenant);
            var handler = new ObterPacienteFinanceiroResumoQueryHandler(db, db);
            ObterPacienteFinanceiroResumoQuery Query(int patientId, int? teamId, int? userId = null) =>
                new(patientId, userId ?? team.UsuarioLoginId, Perfil.EquipeId) { CurrentEquipeId = teamId };

            foreach (var patient in patients)
            {
                var result = await handler.Handle(Query(patient.Id, team.Id), default);
                Assert.Equal(150m, result.ValorApresentado);
                Assert.Equal(20m, result.ValorGlosado);
                Assert.Equal(130m, result.ValorReconhecido);
                Assert.Equal(normalized ? 50m : 130m, result.ValorRecebido);
                Assert.Equal(normalized ? 80m : 0m, result.SaldoAberto);
                Assert.Equal(normalized ? "Normalizado" : "Legado", result.OrigemDados);
                Assert.Equal(normalized ? "Parcialmente recebido" : "Recebido (legado)", result.StatusFinanceiro);
                Assert.Empty(result.Avisos);
            }
            var id = patients[0].Id;
            foreach (var query in new[]
            {
                Query(denied.Id, team.Id), // An attendance alone does not grant team access.
                Query(id, null), Query(id, int.MaxValue),
                Query(id, otherTeam.Id), Query(id, team.Id, otherTeam.UsuarioLoginId),
                Query(id, foreignTeam.Id, foreignTeam.UsuarioLoginId)
            })
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(query, default));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(Query(foreignPatient.Id, team.Id), default));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(Query(int.MaxValue, team.Id), default));

            // Existing profile rules remain effective; administrators still cannot cross tenants.
            foreach (var profile in new[] { Perfil.AdministradorId, Perfil.SuperAdministradorId, Perfil.ControllerId, Perfil.MedicosId, Perfil.PacientesId })
            {
                var userId = profile == Perfil.PacientesId ? patients[0].UserId : doctorId;
                Assert.Equal(150m, (await handler.Handle(new(id, userId, profile), default)).ValorApresentado);
                await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(new(foreignPatient.Id, userId, profile), default));
            }
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new(id, otherTeam.UsuarioLoginId, Perfil.MedicosId), default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new(id, denied.UserId, Perfil.PacientesId), default));
            team.Ativa = false;
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(Query(id, team.Id), default));
            team.Ativa = true;
            team.Membros.Single().Ativo = false;
            await setup.SaveChangesAsync();
            foreach (var patient in patients)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(Query(patient.Id, team.Id), default));

            tenant.SetCurrent(otherClinic.Id, otherClinic.Slug);
            Assert.Equal(150m, (await handler.Handle(Query(foreignPatient.Id, foreignTeam.Id, foreignTeam.UsuarioLoginId), default)).ValorApresentado);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(Query(id, foreignTeam.Id, foreignTeam.UsuarioLoginId), default));
            Assert.Empty(db.ChangeTracker.Entries());
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static Equipe Team(int clinicId, string key) => new()
    {
        ClinicaId = clinicId, Nome = $"Synthetic team {key}", UsuarioLogin = User(clinicId, $"team-{key}", Perfil.EquipeId),
        Membros = [new EquipeMembro { ClinicaId = clinicId, User = User(clinicId, $"doctor-{key}", Perfil.MedicosId) }]
    };

    private static User User(int clinicId, string key, int profile) => new()
    {
        ClinicaId = clinicId, Nome = $"Synthetic {key}", Email = $"{key}@example.com",
        Telefone = "+5511999999999", Senha = "test-hash", PerfilId = profile
    };

    private static Paciente Patient(int clinicId, string key) => new()
    {
        ClinicaId = clinicId, NomePaciente = $"Synthetic {key}", User = User(clinicId, key, Perfil.PacientesId),
        DataAtendimento = new DateTime(2030, 1, 1), Pagamento = "150.00", RepasseGlosa = "20.00", StatusPago = true
    };
}
