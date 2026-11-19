using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

// Regra: cadastro de SuperAdministrador só pode ser alterado, inativado ou excluído por outro SuperAdministrador.
// Perfis inferiores apenas consultam. A tela esconde as ações, mas a garantia é do backend.
public sealed class SuperAdministradorProtectionEndpointTests
{
    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.ControllerId)]
    [InlineData(Perfil.MedicosId)]
    public async Task LowerProfiles_CannotUpdateDeleteOrResetSuperAdministrator(int actorProfileId)
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var target = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.SuperAdministradorId, "alvo");
        var actor = await CreateUserAsync(factory, Clinica.DefaultId, actorProfileId, "ator");
        using var client = CreateBearerClient(factory, actor.Token);

        var update = await client.PutAsJsonAsync($"/api/users/{target.User.Id}", UpdatePayload(target.User, "Nome alterado", ativo: false));
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        // Rebaixar e inativar: perfil enviado é atribuível pelo ator, então só a regra do SuperAdministrador barra.
        var demote = await client.PutAsJsonAsync($"/api/users/{target.User.Id}",
            UpdatePayload(target.User, "Rebaixado", ativo: false, perfilId: Perfil.AdministradorId));
        Assert.Equal(HttpStatusCode.Forbidden, demote.StatusCode);

        var delete = await client.DeleteAsync($"/api/users/{target.User.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        var reset = await client.PutAsJsonAsync($"/api/users/{target.User.Id}/password/reset", new { });
        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);

        var stored = await LoadUserAsync(factory, target.User.Id);
        Assert.Equal(target.User.Nome, stored.Nome);
        Assert.True(stored.Ativo);
        Assert.Equal(Perfil.SuperAdministradorId, stored.PerfilId);
    }

    [Fact]
    public async Task Administrator_CanStillViewSuperAdministrator()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var target = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.SuperAdministradorId, "alvo");
        var admin = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.AdministradorId, "admin");
        using var client = CreateBearerClient(factory, admin.Token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/users/{target.User.Id}")).StatusCode);
        var list = await client.GetAsync("/api/users/");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(target.User.Email, await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SuperAdministrator_CanUpdateAndInactivateAnotherSuperAdministrator()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var target = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.SuperAdministradorId, "alvo");
        var super = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.SuperAdministradorId, "super");
        using var client = CreateBearerClient(factory, super.Token);

        var update = await client.PutAsJsonAsync($"/api/users/{target.User.Id}", UpdatePayload(target.User, "Nome pelo super", ativo: true));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Nome pelo super", (await LoadUserAsync(factory, target.User.Id)).Nome);

        Assert.True((await client.DeleteAsync($"/api/users/{target.User.Id}")).IsSuccessStatusCode);
        Assert.False((await LoadUserAsync(factory, target.User.Id)).Ativo);
    }

    [Fact]
    public async Task AdministratorOfAnotherClinic_GetsNotFoundForSuperAdministrator()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var target = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.SuperAdministradorId, "alvo");
        int otherClinicId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var clinic = new Clinica { Nome = "Clinica B", Slug = $"clinica-b-{Guid.NewGuid():N}"[..20] };
            db.Clinicas.Add(clinic);
            await db.SaveChangesAsync();
            otherClinicId = clinic.Id;
        }
        var otherAdmin = await CreateUserAsync(factory, otherClinicId, Perfil.AdministradorId, "admin-b");
        using var client = CreateBearerClient(factory, otherAdmin.Token);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/users/{target.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/users/{target.User.Id}", UpdatePayload(target.User, "Invasao", ativo: false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/users/{target.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/users/{target.User.Id}/password/reset", new { })).StatusCode);

        var stored = await LoadUserAsync(factory, target.User.Id);
        Assert.Equal(target.User.Nome, stored.Nome);
        Assert.True(stored.Ativo);
    }

    // Mesma pessoa: SuperAdministrador na clínica padrão e Médico na Clínica B. O Administrador da B não pode
    // alterar nem inativar esse cadastro (o nome é global); o próprio titular continua editando o seu.
    [Fact]
    public async Task SuperAdministradorInAnotherClinic_IsProtectedFromLowerProfilesHere()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var clinicB = await CreateClinicAsync(factory);
        var superHere = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.SuperAdministradorId, "global");
        var sameInB = await CreateUserAsync(factory, clinicB, Perfil.MedicosId, "global", superHere.User.Email);
        var adminB = await CreateUserAsync(factory, clinicB, Perfil.AdministradorId, "admin-b");

        using (var admin = CreateBearerClient(factory, adminB.Token))
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/users/{sameInB.User.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/users/{sameInB.User.Id}",
                UpdatePayload(sameInB.User, "Nome trocado pela B", ativo: false))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/users/{sameInB.User.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await admin.PutAsJsonAsync($"/api/users/{sameInB.User.Id}/password/reset", new { })).StatusCode);
        }

        var stored = await LoadUserAsync(factory, sameInB.User.Id);
        Assert.Equal(sameInB.User.Nome, stored.Nome);
        Assert.True(stored.Ativo);

        using var self = CreateBearerClient(factory, sameInB.Token);
        var ownUpdate = await self.PutAsJsonAsync($"/api/users/{sameInB.User.Id}",
            new { nome = "Nome pelo titular", email = sameInB.User.Email, telefone = sameInB.User.Telefone,
                perfilId = Perfil.MedicosId, ativo = true, crm = "12345", crmUf = "SP" });
        Assert.Equal(HttpStatusCode.OK, ownUpdate.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanStillManageUsersWithoutSuperAdministratorAnywhere()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var medico = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.MedicosId, "medico");
        var admin = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.AdministradorId, "admin");
        using var client = CreateBearerClient(factory, admin.Token);

        var update = await client.PutAsJsonAsync($"/api/users/{medico.User.Id}",
            new { nome = "Medico atualizado", email = medico.User.Email, telefone = medico.User.Telefone,
                perfilId = Perfil.MedicosId, ativo = true, crm = "12345", crmUf = "SP" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.True((await client.DeleteAsync($"/api/users/{medico.User.Id}")).IsSuccessStatusCode);
    }

    // POST /api/users com e-mail de cadastro inativo reaproveita o registro (perfil, nome, senha, reativação global).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Administrator_CannotReactivateSuperAdministratorThroughCreate(bool superInAnotherClinic)
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        (User User, string Token) inactive;
        if (superInAnotherClinic)
        {
            var clinicB = await CreateClinicAsync(factory);
            var superB = await CreateUserAsync(factory, clinicB, Perfil.SuperAdministradorId, "global");
            inactive = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.MedicosId, "global", superB.User.Email);
        }
        else
        {
            inactive = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.SuperAdministradorId, "inativo");
        }
        await SetActiveAsync(factory, inactive.User.Id, false);
        var admin = await CreateUserAsync(factory, Clinica.DefaultId, Perfil.AdministradorId, "admin");
        using var client = CreateBearerClient(factory, admin.Token);

        var create = await client.PostAsJsonAsync("/api/users/", new
        {
            nome = "Reativado pelo admin", email = inactive.User.Email, telefone = "+5511988887777",
            perfilId = Perfil.AdministradorId
        });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var stored = await LoadUserAsync(factory, inactive.User.Id);
        Assert.False(stored.Ativo);
        Assert.Equal(inactive.User.Nome, stored.Nome);
        Assert.Equal(inactive.User.PerfilId, stored.PerfilId);
    }

    private static async Task SetActiveAsync(HemodinksApiFactory factory, int id, bool ativo)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await context.Users.IgnoreQueryFilters().SingleAsync(item => item.Id == id);
        user.Ativo = ativo;
        await context.SaveChangesAsync();
    }

    private static async Task<int> CreateClinicAsync(HemodinksApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var clinic = new Clinica { Nome = "Clinica B", Slug = $"clinica-b-{Guid.NewGuid():N}"[..20] };
        db.Clinicas.Add(clinic);
        await db.SaveChangesAsync();
        return clinic.Id;
    }

    private static object UpdatePayload(User user, string nome, bool ativo, int? perfilId = null) => new
    {
        nome,
        email = user.Email,
        telefone = user.Telefone,
        perfilId = perfilId ?? user.PerfilId,
        ativo
    };

    private static async Task<User> LoadUserAsync(HemodinksApiFactory factory, int id)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id == id);
    }

    private static async Task<(User User, string Token)> CreateUserAsync(
        HemodinksApiFactory factory, int clinicaId, int perfilId, string label, string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var user = new User
        {
            ClinicaId = clinicaId,
            Nome = $"Usuario {label}",
            Email = email ?? $"{label}-{Guid.NewGuid():N}@example.com",
            Telefone = "+5511999990000",
            Senha = "hash-nao-utilizado",
            Crm = perfilId == Perfil.MedicosId ? "12345" : null,
            CrmUf = perfilId == Perfil.MedicosId ? "SP" : null,
            DataCadastro = DateTime.UtcNow,
            Ativo = true,
            PrecisaTrocarSenha = false,
            PerfilId = perfilId
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var loaded = await context.Users.IgnoreQueryFilters()
            .Include(item => item.Perfil).Include(item => item.Clinica)
            .SingleAsync(item => item.Id == user.Id);
        var membership = await GlobalIdentityService.EnsureForUserAsync(context, loaded, CancellationToken.None);
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .GenerateToken(membership.UsuarioGlobal, membership, loaded);
        return (loaded, token);
    }

    private static HttpClient CreateBearerClient(HemodinksApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
