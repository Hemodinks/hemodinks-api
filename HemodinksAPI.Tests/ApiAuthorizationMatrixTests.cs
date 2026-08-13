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

public partial class ApiEndpointIntegrationTests
{
    private static readonly int[] AllProfiles =
    [
        Perfil.AdministradorId,
        Perfil.MedicosId,
        Perfil.PacientesId,
        Perfil.ControllerId,
        Perfil.SuperAdministradorId
    ];

    private static readonly int[] Administrators = [Perfil.AdministradorId, Perfil.SuperAdministradorId];
    private static readonly int[] AdministratorsAndController =
        [Perfil.AdministradorId, Perfil.ControllerId, Perfil.SuperAdministradorId];
    private static readonly int[] ClinicalOperators =
    [
        Perfil.AdministradorId,
        Perfil.MedicosId,
        Perfil.ControllerId,
        Perfil.SuperAdministradorId
    ];

    [Fact]
    public async Task ApiCrudAuthorizationMatrix_MatchesDocumentedProfileContract()
    {
        using var factory = new HemodinksApiFactory();
        using var startupClient = factory.CreateClient();
        var identities = await CreateProfileIdentitiesAsync(factory);
        var clients = identities.ToDictionary(
            item => item.Key,
            item => CreateBearerClient(factory, item.Value.Token));

        try
        {
            foreach (var probe in BuildAuthorizationProbes())
            {
                foreach (var profileId in AllProfiles)
                {
                    using var request = BuildProbeRequest(probe, identities[profileId]);
                    using var response = await clients[profileId].SendAsync(request);
                    var expectedAllowed = probe.AllowedProfiles.Contains(profileId);
                    var authorizationRejected = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
                    var responseBody = await response.Content.ReadAsStringAsync();

                    Assert.True(
                        expectedAllowed ? !authorizationRejected : response.StatusCode == HttpStatusCode.Forbidden,
                        $"{probe.Resource}/{probe.Operation} para perfil {ProfileName(profileId)} retornou {(int)response.StatusCode}: {responseBody}");
                    Assert.False(
                        expectedAllowed && response.StatusCode == HttpStatusCode.InternalServerError,
                        $"{probe.Resource}/{probe.Operation} passou pela autorizacao, mas retornou 500 para {ProfileName(profileId)}: {responseBody}");
                }
            }
        }
        finally
        {
            foreach (var client in clients.Values)
            {
                client.Dispose();
            }
        }
    }

    private static IReadOnlyList<AuthorizationProbe> BuildAuthorizationProbes()
    {
        return
        [
            Probe("Dashboard", "Visualizar", HttpMethod.Get, "/api/dashboard/summary", AllProfiles),
            Probe("Clinicas", "Listar", HttpMethod.Get, "/api/platform/clinicas", Administrators),
            Probe("Clinicas", "Cadastrar", HttpMethod.Post, "/api/platform/clinicas", [Perfil.SuperAdministradorId], BodyKind.EmptyJson),
            Probe("Clinicas", "Alterar", HttpMethod.Put, "/api/platform/clinicas/999999", [Perfil.SuperAdministradorId], BodyKind.EmptyJson),
            Probe("Clinicas", "Desativar", HttpMethod.Delete, "/api/platform/clinicas/999999", [Perfil.SuperAdministradorId]),
            Probe("Auditoria", "Visualizar", HttpMethod.Get, "/api/platform/auditoria", [Perfil.SuperAdministradorId]),

            Probe("Usuarios", "Listar", HttpMethod.Get, "/api/users/", Administrators),
            Probe("Usuarios", "Cadastrar", HttpMethod.Post, "/api/users/", Administrators, BodyKind.EmptyJson),
            Probe("Usuarios", "Visualizar proprio", HttpMethod.Get, "/api/users/{self}", AllProfiles),
            Probe("Usuarios", "Alterar proprio", HttpMethod.Put, "/api/users/{self}",
                [Perfil.AdministradorId, Perfil.MedicosId, Perfil.PacientesId, Perfil.SuperAdministradorId], BodyKind.ValidOwnUser),
            Probe("Usuarios", "Excluir", HttpMethod.Delete, "/api/users/999999", Administrators),
            Probe("Usuarios", "Resetar senha de terceiro", HttpMethod.Put, "/api/users/999999/password/reset", Administrators),

            Probe("Pacientes", "Listar", HttpMethod.Get, "/api/pacientes/", AllProfiles),
            Probe("Pacientes", "Cadastrar", HttpMethod.Post, "/api/pacientes/", ClinicalOperators, BodyKind.EmptyJson),
            Probe("Pacientes", "Alterar", HttpMethod.Put, "/api/pacientes/999999", ClinicalOperators, BodyKind.EmptyJson),
            Probe("Pacientes", "Excluir", HttpMethod.Delete, "/api/pacientes/999999", Administrators),

            Probe("Faturamento medico", "Visualizar", HttpMethod.Get, "/api/faturamentos-medicos/", ClinicalOperators),

            Probe("Grupos medicos", "Listar", HttpMethod.Get, "/api/grupos-medicos/", AdministratorsAndController),
            Probe("Grupos medicos", "Cadastrar", HttpMethod.Post, "/api/grupos-medicos/",
                [Perfil.AdministradorId, Perfil.ControllerId, Perfil.SuperAdministradorId], BodyKind.EmptyJson),
            Probe("Grupos medicos", "Alterar", HttpMethod.Put, "/api/grupos-medicos/999999", AdministratorsAndController, BodyKind.EmptyJson),
            Probe("Grupos medicos", "Excluir", HttpMethod.Delete, "/api/grupos-medicos/999999", AdministratorsAndController),

            Probe("Agenda", "Listar", HttpMethod.Get, "/api/events/", AllProfiles),
            Probe("Agenda", "Cadastrar", HttpMethod.Post, "/api/events/", AllProfiles, BodyKind.EmptyJson),
            Probe("Agenda", "Alterar", HttpMethod.Put, "/api/events/999999", AllProfiles, BodyKind.EmptyJson),
            Probe("Agenda", "Excluir", HttpMethod.Delete, "/api/events/999999", AllProfiles),

            Probe("CBHPM", "Visualizar", HttpMethod.Get, "/api/cbhpm/", AllProfiles),
            Probe("CBHPM", "Importar", HttpMethod.Post, "/api/cbhpm/import", Administrators, BodyKind.EmptyJson),
            Probe("Catalogos clinicos", "Listar hospitais", HttpMethod.Get, "/api/hospitais/", AllProfiles),
            Probe("Catalogos clinicos", "Listar convenios", HttpMethod.Get, "/api/convenios/", AllProfiles),
            Probe("Catalogos clinicos", "Listar OPME", HttpMethod.Get, "/api/opme/", AllProfiles),

            Probe("Licencas", "Visualizar propria", HttpMethod.Get, "/api/licencas/current", AllProfiles),
            Probe("Licencas", "Visualizar de medico", HttpMethod.Get, "/api/licencas/users/999999", Administrators),
            Probe("Licencas", "Alterar de medico", HttpMethod.Put, "/api/licencas/users/999999", Administrators, BodyKind.EmptyJson),
            Probe("Exportacoes", "Solicitar", HttpMethod.Post, "/api/exports", AllProfiles, BodyKind.EmptyJson)
        ];
    }

    private static AuthorizationProbe Probe(
        string resource,
        string operation,
        HttpMethod method,
        string path,
        IReadOnlyCollection<int> allowedProfiles,
        BodyKind bodyKind = BodyKind.None)
    {
        return new AuthorizationProbe(resource, operation, method, path, allowedProfiles, bodyKind);
    }

    private static HttpRequestMessage BuildProbeRequest(AuthorizationProbe probe, ProfileIdentity identity)
    {
        var request = new HttpRequestMessage(
            probe.Method,
            probe.Path.Replace("{self}", identity.User.Id.ToString(), StringComparison.Ordinal));

        if (probe.BodyKind == BodyKind.EmptyJson)
        {
            request.Content = JsonContent.Create(new { });
        }
        else if (probe.BodyKind == BodyKind.ValidOwnUser)
        {
            request.Content = JsonContent.Create(new
            {
                identity.User.Nome,
                identity.User.Email,
                identity.User.Telefone,
                identity.User.Cpf,
                identity.User.Crm,
                identity.User.CrmUf,
                identity.User.DataNascimento,
                Ativo = true,
                PerfilId = identity.User.PerfilId == Perfil.SuperAdministradorId
                    ? Perfil.AdministradorId
                    : identity.User.PerfilId
            });
        }

        return request;
    }

    private static async Task<IReadOnlyDictionary<int, ProfileIdentity>> CreateProfileIdentitiesAsync(
        HemodinksApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();

        var users = AllProfiles.Select(profileId => new User
        {
            ClinicaId = Clinica.DefaultId,
            Nome = $"Matriz {ProfileName(profileId)}",
            Email = $"matriz-{profileId}-{Guid.NewGuid():N}@example.com",
            Telefone = $"+5511999999{profileId:00}",
            Senha = "hash-nao-utilizado",
            Crm = profileId == Perfil.MedicosId ? "12345" : null,
            CrmUf = profileId == Perfil.MedicosId ? "SP" : null,
            DataNascimento = new DateTime(1990, 1, profileId),
            DataCadastro = DateTime.UtcNow,
            Ativo = true,
            PrecisaTrocarSenha = false,
            PerfilId = profileId
        }).ToList();
        context.Users.AddRange(users);
        await context.SaveChangesAsync();

        var loadedUsers = await context.Users
            .Include(item => item.Perfil)
            .Include(item => item.Clinica)
            .Where(item => users.Select(user => user.Id).Contains(item.Id))
            .ToListAsync();
        var tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var identities = new Dictionary<int, ProfileIdentity>();
        foreach (var user in loadedUsers)
        {
            var membership = await GlobalIdentityService.EnsureForUserAsync(context, user, CancellationToken.None);
            identities[user.PerfilId] = new ProfileIdentity(
                user,
                tokenService.GenerateToken(membership.UsuarioGlobal, membership, user));
        }

        return identities;
    }

    private static HttpClient CreateBearerClient(HemodinksApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string ProfileName(int profileId)
    {
        return profileId switch
        {
            Perfil.AdministradorId => "Administrador",
            Perfil.MedicosId => "Medico",
            Perfil.PacientesId => "Paciente",
            Perfil.ControllerId => "Controller",
            Perfil.SuperAdministradorId => "SuperAdministrador",
            _ => $"Perfil {profileId}"
        };
    }

    private sealed record AuthorizationProbe(
        string Resource,
        string Operation,
        HttpMethod Method,
        string Path,
        IReadOnlyCollection<int> AllowedProfiles,
        BodyKind BodyKind);

    private sealed record ProfileIdentity(User User, string Token);

    private enum BodyKind
    {
        None,
        EmptyJson,
        ValidOwnUser
    }
}
