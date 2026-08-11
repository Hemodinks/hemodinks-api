using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public class EquipeMedicalUserScopeTests
{
    [Fact]
    public async Task ScopedUsers_WhenLoggedAsTeam_ReturnsAllActiveTeamMembers()
    {
        await using var context = TestDbContextFactory.Create();
        var teamLogin = CreateUser("Equipe Cirurgica", "equipe@hemodinks.com", Perfil.EquipeId);
        var doctor = CreateUser("Dra. Ana", "ana@hemodinks.com", Perfil.MedicosId);
        var controller = CreateUser("Bruno Controller", "bruno@hemodinks.com", Perfil.ControllerId);
        var nominalMember = CreateUser("Clara Equipe", "clara@hemodinks.com", Perfil.EquipeId);
        var inactiveMember = CreateUser("Daniel Inativo", "daniel@hemodinks.com", Perfil.EquipeId, ativo: false);
        var outsider = CreateUser("Eduardo Externo", "eduardo@hemodinks.com", Perfil.MedicosId);
        var team = new Equipe
        {
            ClinicaId = Clinica.DefaultId,
            Nome = "Equipe Cirurgica",
            UsuarioLogin = teamLogin,
            ModoIdentificacao = EquipeModosIdentificacao.Selecao,
            Membros =
            [
                new EquipeMembro { ClinicaId = Clinica.DefaultId, User = doctor },
                new EquipeMembro { ClinicaId = Clinica.DefaultId, User = controller },
                new EquipeMembro { ClinicaId = Clinica.DefaultId, User = nominalMember },
                new EquipeMembro { ClinicaId = Clinica.DefaultId, User = inactiveMember }
            ]
        };
        context.Users.Add(outsider);
        context.Equipes.Add(team);
        await context.SaveChangesAsync();

        var handler = new GetScopedMedicalUsersQueryHandler(context);
        var result = await handler.Handle(new GetScopedMedicalUsersQuery
        {
            CurrentPerfilId = Perfil.EquipeId,
            CurrentUserId = teamLogin.Id,
            CurrentEquipeId = team.Id
        }, CancellationToken.None);

        Assert.Equal(["Bruno Controller", "Clara Equipe", "Dra. Ana"], result.Select(user => user.Nome));
        Assert.DoesNotContain(result, user => user.Nome == inactiveMember.Nome);
        Assert.DoesNotContain(result, user => user.Nome == outsider.Nome);
    }

    private static User CreateUser(string nome, string email, int perfilId, bool ativo = true)
    {
        return new User
        {
            ClinicaId = Clinica.DefaultId,
            Nome = nome,
            Email = email,
            Telefone = "+5581999999999",
            Senha = "hash",
            PerfilId = perfilId,
            Ativo = ativo
        };
    }
}
