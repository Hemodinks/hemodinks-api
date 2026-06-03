using HemodinksAPI.Api.Features.Pacientes.Queries;
using HemodinksAPI.Api.Features.Users.Queries;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class ListOrderingTests
{
    [Fact]
    public async Task GetAllUsers_OrdersByLatestRecordActivityThenName()
    {
        await using var context = TestDbContextFactory.Create();
        context.Users.AddRange(
            CreateUser("Carlos Antigo", "carlos@hemodinks.com", "52998224725", new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 21, 9, 0, 0, DateTimeKind.Utc)),
            CreateUser("Bruno Recente", "bruno@hemodinks.com", "11144477735", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc)),
            CreateUser("Ana Recente", "ana@hemodinks.com", "93541134780", new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc)));
        await context.SaveChangesAsync();

        var handler = new GetAllUsersQueryHandler(context, NullLogger<GetAllUsersQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllUsersQuery { Page = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal(["Ana Recente", "Bruno Recente", "Carlos Antigo"], result.Items.Select(user => user.Nome));
    }

    [Fact]
    public async Task GetAllPacientes_OrdersByLatestLinkedUserActivityThenName()
    {
        await using var context = TestDbContextFactory.Create();
        var antigo = CreateUser("Zelia Antiga", "zelia@hemodinks.com", "52998224725", new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);
        var bruno = CreateUser("Bruno Recente", "bruno.paciente@hemodinks.com", "11144477735", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc), Perfil.PacientesId);
        var ana = CreateUser("Ana Recente", "ana.paciente@hemodinks.com", "93541134780", new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc), Perfil.PacientesId);

        context.Pacientes.AddRange(
            new Paciente { User = antigo, NomePaciente = antigo.Nome },
            new Paciente { User = bruno, NomePaciente = bruno.Nome },
            new Paciente { User = ana, NomePaciente = ana.Nome });
        await context.SaveChangesAsync();

        var handler = new GetAllPacientesQueryHandler(context, NullLogger<GetAllPacientesQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllPacientesQuery
        {
            Page = 1,
            PageSize = 10,
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        Assert.Equal(["Ana Recente", "Bruno Recente", "Zelia Antiga"], result.Items.Select(paciente => paciente.NomePaciente));
    }

    private static User CreateUser(
        string nome,
        string email,
        string cpf,
        DateTime dataCadastro,
        DateTime? dataAtualizacao,
        int perfilId = Perfil.MedicosId)
    {
        return new User
        {
            Nome = nome,
            Email = email,
            Telefone = "+5511999999999",
            Cpf = cpf,
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataCadastro = dataCadastro,
            DataAtualizacao = dataAtualizacao,
            DataNascimento = new DateTime(1990, 1, 1),
            Ativo = true,
            PrecisaTrocarSenha = true,
            PerfilId = perfilId
        };
    }
}
