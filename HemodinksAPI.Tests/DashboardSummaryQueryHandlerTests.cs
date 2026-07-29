using HemodinksAPI.Application.Features.Dashboard.Queries;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class DashboardSummaryQueryHandlerTests
{
    [Fact]
    public async Task GetDashboardSummary_SeparatesPatientUsersFromUsersCounter()
    {
        await using var context = TestDbContextFactory.Create();
        var admin = CreateUser("Admin Hemodinks", "admin@hemodinks.com", Perfil.AdministradorId, true);
        var doctor = CreateUser("Dra. Ana", "dra.ana@hemodinks.com", Perfil.MedicosId, true);
        var controller = CreateUser("Controller", "controller@hemodinks.com", Perfil.ControllerId, false);
        var patientUser = CreateUser("Paciente Hemodinks", "paciente@hemodinks.com", Perfil.PacientesId, true);

        context.Users.AddRange(admin, doctor, controller);
        context.Pacientes.Add(new Paciente
        {
            User = patientUser,
            NomePaciente = patientUser.Nome,
            StatusPago = false,
        });
        context.ContasReceber.AddRange(
            CreateAccount("ABERTO", ContaReceberStatus.Aberto),
            CreateAccount("PARCIAL", ContaReceberStatus.ParcialmenteRecebido),
            CreateAccount("VENCIDO", ContaReceberStatus.Vencido),
            CreateAccount("RECEBIDO", ContaReceberStatus.Recebido),
            CreateAccount("CANCELADO", ContaReceberStatus.Cancelado),
            CreateAccount("PREVISTO", ContaReceberStatus.Previsto));
        await context.SaveChangesAsync();

        var handler = new GetDashboardSummaryQueryHandler(
            context,
            NullLogger<GetDashboardSummaryQueryHandler>.Instance);

        var result = await handler.Handle(new GetDashboardSummaryQuery
        {
            CurrentPerfilId = Perfil.AdministradorId,
            CurrentUserId = admin.Id,
        }, CancellationToken.None);

        Assert.Equal(3, result.UsersCount);
        Assert.Equal(2, result.ActiveUsersCount);
        Assert.Equal(1, result.PacientesCount);
        Assert.Equal(1, result.ActivePatientsCount);
        Assert.Equal(3, result.PendingPaymentsCount);
    }

    private static User CreateUser(string nome, string email, int perfilId, bool ativo)
    {
        return new User
        {
            Nome = nome,
            Email = email,
            Telefone = "+5511999999999",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataCadastro = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            DataNascimento = new DateTime(1990, 1, 1),
            Ativo = ativo,
            PrecisaTrocarSenha = false,
            PerfilId = perfilId,
        };
    }

    private static ContaReceber CreateAccount(string documento, ContaReceberStatus status)
    {
        return new ContaReceber
        {
            NumeroDocumento = documento,
            Descricao = $"Titulo {documento}",
            Competencia = new DateTime(2026, 7, 1),
            DataEmissao = new DateTime(2026, 7, 1),
            DataVencimento = new DateTime(2026, 7, 31),
            Status = status
        };
    }
}
