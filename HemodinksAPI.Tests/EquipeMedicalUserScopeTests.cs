using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public class EquipeMedicalUserScopeTests
{
    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.SuperAdministradorId)]
    public async Task ScopedUsers_WhenLoggedAsAdministrator_ReturnsActiveClinicTeamMembers(int perfilId)
    {
        await using var context = TestDbContextFactory.Create();
        var administrator = CreateUser("Administrador", $"admin-{perfilId}@hemodinks.com", perfilId);
        var doctor = CreateUser("Dra. Ana", "ana.admin@hemodinks.com", Perfil.MedicosId);
        var controllerMember = CreateUser("Bruno Controller", "bruno.admin@hemodinks.com", Perfil.ControllerId);
        var nominalMember = CreateUser("Clara Equipe", "clara.admin@hemodinks.com", Perfil.EquipeId);
        var inactiveMembership = CreateUser("Daniel Fora da Equipe", "daniel.admin@hemodinks.com", Perfil.EquipeId);
        var inactiveTeamMember = CreateUser("Elisa Equipe Inativa", "elisa.admin@hemodinks.com", Perfil.EquipeId);
        var outsider = CreateUser("Fabio Externo", "fabio.admin@hemodinks.com", Perfil.EquipeId);

        context.Users.AddRange(administrator, doctor, outsider);
        context.Equipes.AddRange(
            new Equipe
            {
                ClinicaId = Clinica.DefaultId,
                Nome = "Equipe Ativa",
                UsuarioLogin = CreateUser("Login Equipe Ativa", "login.ativa@hemodinks.com", Perfil.EquipeId),
                Membros =
                [
                    new EquipeMembro { ClinicaId = Clinica.DefaultId, User = controllerMember },
                    new EquipeMembro { ClinicaId = Clinica.DefaultId, User = nominalMember },
                    new EquipeMembro { ClinicaId = Clinica.DefaultId, User = inactiveMembership, Ativo = false }
                ]
            },
            new Equipe
            {
                ClinicaId = Clinica.DefaultId,
                Nome = "Equipe Inativa",
                Ativa = false,
                UsuarioLogin = CreateUser("Login Equipe Inativa", "login.inativa@hemodinks.com", Perfil.EquipeId),
                Membros =
                [
                    new EquipeMembro { ClinicaId = Clinica.DefaultId, User = inactiveTeamMember }
                ]
            });
        await context.SaveChangesAsync();

        var handler = new GetScopedMedicalUsersQueryHandler(context);
        var result = await handler.Handle(new GetScopedMedicalUsersQuery
        {
            CurrentPerfilId = perfilId,
            CurrentUserId = administrator.Id
        }, CancellationToken.None);

        Assert.Equal(["Bruno Controller", "Clara Equipe", "Dra. Ana"], result.Select(user => user.Nome));
        Assert.DoesNotContain(result, user => user.Nome == inactiveMembership.Nome);
        Assert.DoesNotContain(result, user => user.Nome == inactiveTeamMember.Nome);
        Assert.DoesNotContain(result, user => user.Nome == outsider.Nome);
    }

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
