using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Clinics.Platform;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

public sealed class PlatformActiveSessionsEndpointTests : ApiEndpointTestBase
{
    private const string Route = "/api/platform/sessoes-ativas";
    private const string SecretRefreshHash = "refresh-hash-que-nunca-pode-vazar";
    private const string SessionIp = "203.0.113.77";
    private const string DistinctiveUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 marcador-ua-unico";

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.MedicosId)]
    [InlineData(Perfil.ControllerId)]
    [InlineData(Perfil.PacientesId)]
    public async Task NonSuperAdministrator_IsForbidden_EvenForOwnClinic(int profileId)
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var seed = await SeedAsync(factory);

        using var client = CreateBearerClient(factory, profileId == Perfil.AdministradorId
            ? seed.AdminToken
            : await TokenForNewUserAsync(factory, Clinica.DefaultId, profileId));
        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Medico Beta Online", body);
        Assert.DoesNotContain("SuperAdmin Online", body);
    }

    [Fact]
    public async Task AdministratorOfOtherClinic_IsForbidden_AndSeesNothingOfClinicA()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var seed = await SeedAsync(factory);

        using var client = CreateBearerClient(factory, seed.BetaAdminToken);
        using var response = await client.GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("SuperAdmin Online", body);
        Assert.DoesNotContain("Clinica", body);
    }

    [Fact]
    public async Task SuperAdministrator_WithOtherClinicHeader_StillSeesGlobalList()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var seed = await SeedAsync(factory);

        using var client = CreateBearerClient(factory, seed.SuperAdminToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, Route);
        request.Headers.Add(HemodinksAPI.Api.ClinicaResolutionService.ClinicaSlugHeaderName, seed.BetaSlug);
        using var response = await client.SendAsync(request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var names = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("nome").GetString()).ToList();
        Assert.Contains("SuperAdmin Online", names);
        Assert.Contains("Medico Beta Online", names);
    }

    [Fact]
    public async Task SuperAdministrator_SeesOnlyActiveSessionsOfAllClinics_WithoutSensitiveData()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var seed = await SeedAsync(factory);

        using var client = CreateBearerClient(factory, seed.SuperAdminToken);
        using var response = await client.GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var names = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("nome").GetString())
            .ToList();

        Assert.Contains("SuperAdmin Online", names);
        Assert.Contains("Medico Beta Online", names);
        Assert.DoesNotContain("Admin Revogado", names);
        Assert.DoesNotContain("Admin Ocioso", names);
        Assert.DoesNotContain("Admin Expirado", names);
        Assert.DoesNotContain("Admin Versao Antiga", names);
        Assert.DoesNotContain("Usuario Inativo", names);
        Assert.DoesNotContain("Vinculo Inativo", names);
        Assert.DoesNotContain("Login Equipe Sem Equipe", names);

        var clinics = json.RootElement.GetProperty("porClinica").EnumerateArray()
            .Select(item => item.GetProperty("clinicaId").GetInt32())
            .ToList();
        Assert.Contains(Clinica.DefaultId, clinics);
        Assert.Contains(seed.BetaClinicId, clinics);
        Assert.Equal(2, json.RootElement.GetProperty("usuariosOnline").GetInt32());

        var beta = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("nome").GetString() == "Medico Beta Online");
        Assert.Equal("Chrome · Windows", beta.GetProperty("navegador").GetString());

        Assert.DoesNotContain(SecretRefreshHash, body);
        Assert.DoesNotContain(SessionIp, body);
        Assert.DoesNotContain("marcador-ua-unico", body);
        Assert.DoesNotContain("@example.com", body);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityVersion", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PageSize_IsClamped()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var seed = await SeedAsync(factory);

        using var client = CreateBearerClient(factory, seed.SuperAdminToken);
        using var response = await client.GetAsync($"{Route}?pagina=0&tamanhoPagina=5000");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.RootElement.GetProperty("pagina").GetInt32());
        Assert.Equal(200, json.RootElement.GetProperty("tamanhoPagina").GetInt32());
    }

    [Theory]
    [InlineData(null, "Desconhecido")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/130.0 Safari/537.36 Edg/130.0", "Edge · Windows")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Version/17.0 Mobile/15E148 Safari/604.1", "Safari · iOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/130.0 Mobile Safari/537.36", "Chrome · Android")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0", "Firefox · Linux")]
    public void UserAgentSummary_IsCoarse(string? userAgent, string expected)
    {
        Assert.Equal(expected, UserAgentSummary.Describe(userAgent));
    }

    [Fact]
    public async Task Snapshot_CountsSameActiveSessionsAsList_ByClinicId()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var seed = await SeedAsync(factory);

        using var scope = factory.Services.CreateScope();
        var snapshot = await scope.ServiceProvider.GetRequiredService<PlatformActiveSessionsQueryHandler>()
            .GetSnapshotAsync();

        Assert.Equal(2, snapshot.UsuariosOnline);
        Assert.Equal(2, snapshot.TotalSessoes);
        Assert.Equal(
            new[] { Clinica.DefaultId, seed.BetaClinicId }.Order().ToArray(),
            snapshot.PorClinica.Select(item => item.ClinicaId).ToArray());
        Assert.All(snapshot.PorClinica, item =>
        {
            Assert.Equal(1, item.Usuarios);
            Assert.Equal(1, item.Sessoes);
        });
    }

    [Fact]
    public async Task SnapshotService_LogsOnlyAggregatedCounts_WithoutPersonalData()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var seed = await SeedAsync(factory);
        var activity = new RequestActivitySignal(TimeProvider.System);
        activity.MarkActive();
        var logger = new SnapshotLogger();
        var service = new ActiveSessionsSnapshotHostedService(factory.Services, activity,
            new ActiveSessionsSnapshotOptions(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)), logger);

        Assert.True(await service.TryRecordOnceAsync(CancellationToken.None));

        var summary = Assert.Single(logger.Entries, entry => entry.EventId == ActiveSessionsSnapshotHostedService.SnapshotEvent.Id);
        Assert.Equal(LogLevel.Warning, summary.Level);
        Assert.Equal(2, summary.Properties["UsuariosOnline"]);
        Assert.Equal(2, summary.Properties["TotalSessoes"]);
        var clinics = logger.Entries
            .Where(entry => entry.EventId == ActiveSessionsSnapshotHostedService.ClinicSnapshotEvent.Id)
            .Select(entry => (int)entry.Properties["SnapshotClinicaId"]!)
            .Order()
            .ToArray();
        Assert.Equal(new[] { Clinica.DefaultId, seed.BetaClinicId }.Order().ToArray(), clinics);

        var everything = string.Join(" | ", logger.Entries.Select(entry =>
            entry.Message + " " + string.Join(" ", entry.Properties.Select(pair => $"{pair.Key}={pair.Value}"))));
        Assert.DoesNotContain("Online", everything.Replace("UsuariosOnline", string.Empty));
        Assert.DoesNotContain("Medico", everything);
        Assert.DoesNotContain("Clinica Beta", everything);
        Assert.DoesNotContain(Clinica.DefaultNome, everything);
        Assert.DoesNotContain("@example.com", everything);
        Assert.DoesNotContain(SessionIp, everything);
        Assert.DoesNotContain("Chrome", everything);
        Assert.DoesNotContain(SecretRefreshHash, everything);
        Assert.DoesNotContain(seed.BetaSlug, everything);
    }

    [Fact]
    public async Task SnapshotService_WithoutRecentAuthenticatedRequest_DoesNotQueryOrLog()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        await SeedAsync(factory);
        var logger = new SnapshotLogger();
        var service = new ActiveSessionsSnapshotHostedService(factory.Services, new RequestActivitySignal(TimeProvider.System),
            new ActiveSessionsSnapshotOptions(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)), logger);

        Assert.False(await service.TryRecordOnceAsync(CancellationToken.None));
        Assert.Empty(logger.Entries);
    }

    private sealed record SnapshotLogEntry(LogLevel Level, int EventId, string Message, IReadOnlyDictionary<string, object?> Properties);

    private sealed class SnapshotLogger : ILogger<ActiveSessionsSnapshotHostedService>
    {
        public List<SnapshotLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new SnapshotLogEntry(logLevel, eventId.Id, formatter(state, exception), properties));
        }
    }

    private sealed record Seed(string SuperAdminToken, string AdminToken, string BetaAdminToken, int BetaClinicId, string BetaSlug);

    private static async Task<Seed> SeedAsync(HemodinksApiFactory factory)
    {
        var beta = await SeedClinicaBetaAsync(factory);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var now = DateTime.UtcNow;

        var superAdmin = await AddMembershipAsync(context, Clinica.DefaultId, Perfil.SuperAdministradorId, "SuperAdmin Online");
        var admin = await AddMembershipAsync(context, Clinica.DefaultId, Perfil.AdministradorId, "Admin Sem Sessao");
        var betaDoctor = await AddMembershipAsync(context, beta.Id, Perfil.MedicosId, "Medico Beta Online");
        var revoked = await AddMembershipAsync(context, Clinica.DefaultId, Perfil.AdministradorId, "Admin Revogado");
        var idle = await AddMembershipAsync(context, Clinica.DefaultId, Perfil.AdministradorId, "Admin Ocioso");
        var expired = await AddMembershipAsync(context, Clinica.DefaultId, Perfil.AdministradorId, "Admin Expirado");
        var stale = await AddMembershipAsync(context, Clinica.DefaultId, Perfil.AdministradorId, "Admin Versao Antiga");
        var betaAdmin = await AddMembershipAsync(context, beta.Id, Perfil.AdministradorId, "Admin Beta Sem Sessao");
        var inactiveUser = await AddMembershipAsync(context, Clinica.DefaultId, Perfil.AdministradorId, "Usuario Inativo");
        var inactiveMembership = await AddMembershipAsync(context, Clinica.DefaultId, Perfil.AdministradorId, "Vinculo Inativo");
        var teamLoginWithoutTeam = await AddMembershipAsync(context, Clinica.DefaultId, Perfil.EquipeId, "Login Equipe Sem Equipe");
        inactiveUser.User.Ativo = false;
        inactiveMembership.Ativo = false;

        context.AuthenticationSessions.AddRange(
            NewSession(superAdmin, now.AddMinutes(-20), now.AddMinutes(-1)),
            NewSession(betaDoctor, now.AddHours(-2), now.AddMinutes(-5)),
            NewSession(revoked, now.AddMinutes(-10), now.AddMinutes(-1), revokedAt: now.AddMinutes(-1)),
            NewSession(idle, now.AddHours(-1), now.AddMinutes(-31)),
            NewSession(expired, now.AddHours(-13), now.AddMinutes(-1)),
            NewSession(stale, now.AddMinutes(-10), now.AddMinutes(-1), securityVersion: Guid.NewGuid()),
            NewSession(inactiveUser, now.AddMinutes(-10), now.AddMinutes(-1)),
            NewSession(inactiveMembership, now.AddMinutes(-10), now.AddMinutes(-1)),
            NewSession(teamLoginWithoutTeam, now.AddMinutes(-10), now.AddMinutes(-1)));
        await context.SaveChangesAsync();

        return new Seed(
            tokens.GenerateToken(superAdmin.UsuarioGlobal, superAdmin, superAdmin.User),
            tokens.GenerateToken(admin.UsuarioGlobal, admin, admin.User),
            tokens.GenerateToken(betaAdmin.UsuarioGlobal, betaAdmin, betaAdmin.User),
            beta.Id,
            beta.Slug);
    }

    private static async Task<string> TokenForNewUserAsync(HemodinksApiFactory factory, int clinicId, int profileId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var membership = await AddMembershipAsync(context, clinicId, profileId, $"Perfil {profileId}");
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .GenerateToken(membership.UsuarioGlobal, membership, membership.User);
    }

    private static async Task<UsuarioClinica> AddMembershipAsync(AppDbContext context, int clinicId, int profileId, string name)
    {
        var user = new User
        {
            ClinicaId = clinicId,
            Nome = name,
            Email = $"sessao-{Guid.NewGuid():N}@example.com",
            Telefone = "+5511977777777",
            Senha = "hash-nao-utilizado",
            Crm = profileId == Perfil.MedicosId ? "54321" : null,
            CrmUf = profileId == Perfil.MedicosId ? "SP" : null,
            DataNascimento = new DateTime(1990, 1, 1),
            DataCadastro = DateTime.UtcNow,
            Ativo = true,
            PrecisaTrocarSenha = false,
            PerfilId = profileId
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        await context.Entry(user).Reference(item => item.Perfil).LoadAsync();
        await context.Entry(user).Reference(item => item.Clinica).LoadAsync();
        return await GlobalIdentityService.EnsureForUserAsync(context, user, CancellationToken.None);
    }

    private static AuthenticationSession NewSession(
        UsuarioClinica membership,
        DateTime createdAt,
        DateTime lastActivityAt,
        DateTime? revokedAt = null,
        Guid? securityVersion = null) => new()
    {
        Id = Guid.NewGuid(),
        UsuarioGlobalId = membership.UsuarioGlobalId,
        UsuarioClinicaId = membership.Id,
        SecurityVersion = securityVersion ?? membership.UsuarioGlobal.SecurityVersion,
        RefreshTokenHash = SecretRefreshHash + Guid.NewGuid().ToString("N"),
        CreatedAt = createdAt,
        LastActivityAt = lastActivityAt,
        RevokedAt = revokedAt,
        CreatedByIp = SessionIp,
        UserAgent = DistinctiveUserAgent
    };

    private static HttpClient CreateBearerClient(HemodinksApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
