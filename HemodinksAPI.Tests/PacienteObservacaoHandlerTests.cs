using HemodinksAPI.Application.Features.Dashboard.Queries;
using HemodinksAPI.Application.Features.Pacientes.Observacoes;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class PacienteObservacaoHandlerTests
{
    [Fact]
    public async Task CreateObservacao_WhenAuthorIsAdmin_SendsToAllMedicalUsersOnPatient()
    {
        await using var context = TestDbContextFactory.Create();
        var admin = CreateUser("Administrador", "admin@hemodinks.com", "52998224725", Perfil.AdministradorId);
        var doctor = CreateUser("Dr. Principal", "principal@hemodinks.com", "11144477735", Perfil.MedicosId);
        var auxiliar = CreateUser("Dra. Auxiliar", "auxiliar@hemodinks.com", "93541134780", Perfil.MedicosId);
        var patientUser = CreateUser("Paciente", "paciente@hemodinks.com", "39053344705", Perfil.PacientesId);
        var paciente = new Paciente
        {
            User = patientUser,
            NomePaciente = "Paciente",
            MedicoUser = doctor,
            Medico = doctor.Nome,
            MedicoAuxiliar1User = auxiliar,
            MedicoAuxiliar1 = auxiliar.Nome
        };

        context.Users.AddRange(admin, doctor, auxiliar, patientUser);
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new CreatePacienteObservacaoCommandHandler(
            context,
            NullLogger<CreatePacienteObservacaoCommandHandler>.Instance);

        var result = await handler.Handle(new CreatePacienteObservacaoCommand
        {
            PacienteId = paciente.Id,
            Texto = "Favor revisar a evolucao do paciente.",
            CurrentUserId = admin.Id,
            CurrentPerfilId = Perfil.AdministradorId,
            CurrentUserName = admin.Nome
        }, CancellationToken.None);

        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(2, context.Observacoes.Count());
        Assert.All(context.Observacoes, observacao =>
        {
            Assert.Equal(admin.Id, observacao.AutorUserId);
            Assert.Equal(paciente.Id, observacao.PacienteId);
            Assert.Equal(doctor.Id, observacao.MedicoUserId);
            Assert.Equal(auxiliar.Id, observacao.MedicoAuxiliar1UserId);
        });
        Assert.Equal(
            [doctor.Id, auxiliar.Id],
            context.Observacoes.Select(observacao => observacao.DestinatarioUserId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task CreateObservacao_WhenReplying_SendsBackToTheOtherParticipant()
    {
        await using var context = TestDbContextFactory.Create();
        var admin = CreateUser("Administrador", "admin@hemodinks.com", "52998224725", Perfil.AdministradorId);
        var doctor = CreateUser("Dr. Principal", "principal@hemodinks.com", "11144477735", Perfil.MedicosId);
        var patientUser = CreateUser("Paciente", "paciente@hemodinks.com", "39053344705", Perfil.PacientesId);
        var paciente = new Paciente
        {
            User = patientUser,
            NomePaciente = "Paciente",
            MedicoUser = doctor,
            Medico = doctor.Nome
        };
        var root = new Observacao
        {
            Paciente = paciente,
            AutorUser = admin,
            DestinatarioUser = doctor,
            Texto = "Observe a pressao no retorno.",
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome
        };

        context.AddRange(admin, doctor, patientUser, paciente, root);
        await context.SaveChangesAsync();

        var handler = new CreatePacienteObservacaoCommandHandler(
            context,
            NullLogger<CreatePacienteObservacaoCommandHandler>.Instance);

        var result = await handler.Handle(new CreatePacienteObservacaoCommand
        {
            PacienteId = paciente.Id,
            ObservacaoPaiId = root.Id,
            Texto = "Paciente reavaliado e orientado.",
            CurrentUserId = doctor.Id,
            CurrentPerfilId = Perfil.MedicosId,
            CurrentUserName = doctor.Nome
        }, CancellationToken.None);

        var reply = context.Observacoes.Single(observacao => observacao.Id != root.Id);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(root.Id, reply.ObservacaoPaiId);
        Assert.Equal(doctor.Id, reply.AutorUserId);
        Assert.Equal(admin.Id, reply.DestinatarioUserId);
    }

    [Fact]
    public async Task GetAllPacientes_ReturnsUnreadObservationCountForCurrentUser()
    {
        await using var context = TestDbContextFactory.Create();
        var admin = CreateUser("Administrador", "admin@hemodinks.com", "52998224725", Perfil.AdministradorId);
        var doctor = CreateUser("Dr. Principal", "principal@hemodinks.com", "11144477735", Perfil.MedicosId);
        var patientUser = CreateUser("Paciente", "paciente@hemodinks.com", "39053344705", Perfil.PacientesId);
        var paciente = new Paciente
        {
            User = patientUser,
            NomePaciente = "Paciente",
            MedicoUser = doctor,
            Medico = doctor.Nome,
            Observacoes =
            [
                new Observacao
                {
                    AutorUser = admin,
                    DestinatarioUser = doctor,
                    Texto = "Primeira observacao"
                },
                new Observacao
                {
                    AutorUser = admin,
                    DestinatarioUser = doctor,
                    Texto = "Segunda observacao",
                    DataLeitura = DateTime.UtcNow
                }
            ]
        };

        context.AddRange(admin, doctor, patientUser, paciente);
        await context.SaveChangesAsync();

        var handler = new GetAllPacientesQueryHandler(context, NullLogger<GetAllPacientesQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllPacientesQuery
        {
            Page = 1,
            PageSize = 10,
            CurrentUserId = doctor.Id,
            CurrentPerfilId = Perfil.MedicosId
        }, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(1, result.Items[0].ObservacoesNaoLidasCount);
    }

    [Fact]
    public async Task GetPacienteObservacoes_ReturnsMostRecentMessagesFirst()
    {
        await using var context = TestDbContextFactory.Create();
        var admin = CreateUser("Administrador", "admin@hemodinks.com", "52998224725", Perfil.AdministradorId);
        var doctor = CreateUser("Dr. Principal", "principal@hemodinks.com", "11144477735", Perfil.MedicosId);
        var patientUser = CreateUser("Paciente", "paciente@hemodinks.com", "39053344705", Perfil.PacientesId);
        var paciente = new Paciente
        {
            User = patientUser,
            NomePaciente = "Paciente",
            MedicoUser = doctor,
            Medico = doctor.Nome,
            Observacoes =
            [
                new Observacao
                {
                    AutorUser = admin,
                    DestinatarioUser = doctor,
                    Texto = "Mensagem mais antiga",
                    DataCadastro = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc)
                },
                new Observacao
                {
                    AutorUser = doctor,
                    DestinatarioUser = admin,
                    Texto = "Mensagem mais recente",
                    DataCadastro = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc)
                }
            ]
        };

        context.AddRange(admin, doctor, patientUser, paciente);
        await context.SaveChangesAsync();

        var handler = new GetPacienteObservacoesQueryHandler(
            context,
            NullLogger<GetPacienteObservacoesQueryHandler>.Instance);

        var result = await handler.Handle(new GetPacienteObservacoesQuery
        {
            PacienteId = paciente.Id,
            CurrentUserId = doctor.Id,
            CurrentPerfilId = Perfil.MedicosId
        }, CancellationToken.None);

        Assert.Equal(
            ["Mensagem mais recente", "Mensagem mais antiga"],
            result.Select(item => item.Texto).ToArray());
    }

    [Fact]
    public async Task DashboardNotifications_IncludeUnreadPatientObservations()
    {
        await using var context = TestDbContextFactory.Create();
        var admin = CreateUser("Administrador", "admin@hemodinks.com", "52998224725", Perfil.AdministradorId);
        var doctor = CreateUser("Dr. Principal", "principal@hemodinks.com", "11144477735", Perfil.MedicosId);
        var patientUser = CreateUser("Paciente", "paciente@hemodinks.com", "39053344705", Perfil.PacientesId);
        var paciente = new Paciente
        {
            User = patientUser,
            NomePaciente = "Paciente",
            MedicoUser = doctor,
            Medico = doctor.Nome
        };
        var observacao = new Observacao
        {
            Paciente = paciente,
            AutorUser = admin,
            DestinatarioUser = doctor,
            Texto = "Favor responder antes do procedimento.",
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome
        };

        context.AddRange(admin, doctor, patientUser, paciente, observacao);
        await context.SaveChangesAsync();

        var summaryHandler = new GetDashboardSummaryQueryHandler(context, NullLogger<GetDashboardSummaryQueryHandler>.Instance);
        var notificationsHandler = new GetDashboardNotificationsQueryHandler(context, NullLogger<GetDashboardNotificationsQueryHandler>.Instance);

        var summary = await summaryHandler.Handle(new GetDashboardSummaryQuery
        {
            CurrentUserId = doctor.Id,
            CurrentPerfilId = Perfil.MedicosId
        }, CancellationToken.None);

        var notifications = await notificationsHandler.Handle(new GetDashboardNotificationsQuery
        {
            CurrentUserId = doctor.Id,
            CurrentPerfilId = Perfil.MedicosId
        }, CancellationToken.None);

        Assert.Equal(1, summary.UnreadObservationCount);
        var notification = Assert.Single(notifications, item => item.Tipo == "ObservacaoPaciente");
        Assert.Equal(observacao.Id, notification.ObservacaoId);
        Assert.Equal(admin.Nome, notification.Autor);
    }

    private static User CreateUser(string nome, string email, string cpf, int perfilId)
    {
        return new User
        {
            Nome = nome,
            Email = email,
            Telefone = "+5511999999999",
            Cpf = cpf,
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            Ativo = true,
            PrecisaTrocarSenha = false,
            PerfilId = perfilId
        };
    }
}
