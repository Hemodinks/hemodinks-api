using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed class TemporaryAccessEndpointTests
{
    [Fact]
    public async Task TemporaryTeamPassword_DoesNotBypassOperatorIdentification()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var admin = await Login(client, TestPasswords.Valid);
        ResetUserPasswordResponse generated;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var teamUser = new HemodinksAPI.Domain.Models.User
            {
                Nome = "Equipe temporária", Email = "temporary-team@example.com", Telefone = "11999999999",
                Senha = new HemodinksAPI.Infrastructure.Utils.PasswordHasher().HashPassword(TestPasswords.Valid),
                PerfilId = HemodinksAPI.Domain.Models.Perfil.EquipeId, ClinicaId = admin.ClinicaId, Ativo = true
            };
            db.Equipes.Add(new HemodinksAPI.Domain.Models.Equipe
            {
                Nome = "Equipe temporária", ClinicaId = admin.ClinicaId, UsuarioLogin = teamUser,
                ModoIdentificacao = HemodinksAPI.Domain.Models.EquipeModosIdentificacao.Pin, Ativa = true
            });
            await db.SaveChangesAsync();
            generated = await scope.ServiceProvider.GetRequiredService<TemporaryAccessService>()
                .GenerateAsync(teamUser.Id, new(admin.Id, admin.PerfilId, admin.Nome, admin.ClinicaId), default);
        }
        var response = await client.PostAsJsonAsync("/api/users/authenticate", new { email = "temporary-team@example.com", senha = generated.SenhaTemporaria });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var challenge = (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        Assert.Null(challenge.Token);
        Assert.NotNull(challenge.EquipeDesafio);
        Assert.True(challenge.PrecisaTrocarSenha);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task GenerationEndpoint_LimitsRepeatedRequestsPerActor()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var user = await Login(client, TestPasswords.Valid);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        for (var index = 0; index < 5; index++)
            Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/users/999999/password/reset", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PutAsJsonAsync("/api/users/999999/password/reset", new { })).StatusCode);
    }

    [Fact]
    public async Task RefreshTokens_AreRevokedAndRestrictedSessionsStayRestricted()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var user = await Login(client, TestPasswords.Valid);
        HemodinksAPI.Application.Features.Sessions.IssuedAuthenticationSession previous;
        using (var initialScope = factory.Services.CreateScope())
        {
            var sessions = initialScope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Features.Sessions.AuthenticationSessionService>();
            previous = (await sessions.StartAsync(user.UsuarioGlobalId, user.Id, user.ClinicaId, null, null, default))!;
        }
        ResetUserPasswordResponse generated;
        using (var generateScope = factory.Services.CreateScope())
        {
            generated = await generateScope.ServiceProvider.GetRequiredService<TemporaryAccessService>()
                .GenerateAsync(user.Id, new(user.Id, user.PerfilId, user.Nome, user.ClinicaId), default);
        }
        using (var refreshScope = factory.Services.CreateScope())
        {
            Assert.Null(await refreshScope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Features.Sessions.AuthenticationSessionService>()
                .RefreshAsync(previous.RefreshToken, default));
        }
        var restricted = await Login(client, generated.SenhaTemporaria!);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var membership = await db.UsuariosClinicas.Include(x => x.UsuarioGlobal).SingleAsync(x => x.UserId == user.Id);
        var service = scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Features.Sessions.AuthenticationSessionService>();
        var recoverySession = (await service.StartAsync(user.UsuarioGlobalId, user.Id, user.ClinicaId, null, null, default, membership.UsuarioGlobal.SecurityVersion))!;
        var refreshed = (await service.RefreshAsync(recoverySession.RefreshToken, default))!;
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(refreshed.AccessToken).Claims, x => x.Type == "temporary_password" && x.Value == "true");
        Assert.True(restricted.PrecisaTrocarSenha);
    }

    [Fact]
    public async Task Recovery_EndToEnd_RevokesTokens_RestrictsApis_AndRequiresNewLogin()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        using (var setup = factory.Services.CreateScope())
        {
            var setupDb = setup.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var seeded = await setupDb.Users.SingleAsync(x => x.Email == "gmarcone@gmail.com");
            seeded.PrecisaTrocarSenha = false;
            await setupDb.SaveChangesAsync();
        }
        var normal = await Login(client, TestPasswords.Valid);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", normal.Token);
        var generatedResponse = await client.PutAsJsonAsync($"/api/users/{normal.Id}/password/reset", new { });
        Assert.Equal(HttpStatusCode.OK, generatedResponse.StatusCode);
        Assert.True(generatedResponse.Headers.CacheControl!.NoStore);
        var generated = (await generatedResponse.Content.ReadFromJsonAsync<ResetUserPasswordResponse>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var restricted = await Login(client, generated.SenhaTemporaria!);
        Assert.True(restricted.PrecisaTrocarSenha);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(restricted.Token);
        Assert.Contains(jwt.Claims, x => x.Type == "temporary_password" && x.Value == "true");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", restricted.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/session/selecionar-clinica", new { clinicaId = 2 })).StatusCode);
        var invalid = await client.PostAsJsonAsync("/api/users/password/temporary/complete", new { novaSenha = "NovaSenha@456", confirmacao = "diferente" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var complete = await client.PostAsJsonAsync("/api/users/password/temporary/complete", new { novaSenha = "NovaSenha@456", confirmacao = "NovaSenha@456" });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.False((await Login(client, "NovaSenha@456")).PrecisaTrocarSenha);
        var reused = await client.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = generated.SenhaTemporaria });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        Assert.Equal(3, await db.AuditoriasPlataforma.CountAsync(x => x.Recurso == "TemporaryAccessCredential"));
    }

    private static async Task<AuthenticateUserResponse> Login(HttpClient client, string password)
    {
        var response = await client.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
    }
}
