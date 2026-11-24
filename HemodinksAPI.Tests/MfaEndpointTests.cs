using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Application.Features.Users.Mfa;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

// #150: verificacao em duas etapas (TOTP) para Administrador e SuperAdministrador.
public sealed class MfaEndpointTests : ApiEndpointTestBase
{
    private const string AdminEmail = "gmarcone@gmail.com";

    [Fact]
    public async Task Admin_EnablesMfa_LoginNeedsSecondFactor_RecoveryCodesAreSingleUse_AndDisableNeedsPasswordAndCode()
    {
        using var factory = new HemodinksApiFactory();
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, Clinica.DefaultSlug);

        using (var status = await ReadJsonAsync(await admin.GetAsync("/api/users/me/mfa")))
        {
            Assert.True(status.RootElement.GetProperty("elegivel").GetBoolean());
            Assert.False(status.RootElement.GetProperty("habilitado").GetBoolean());
        }

        using var setup = await admin.PostAsync("/api/users/me/mfa/configuracao", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Contains("no-store", setup.Headers.CacheControl?.ToString());
        using var setupJson = await ReadJsonAsync(setup);
        var secret = setupJson.RootElement.GetProperty("segredo").GetString()!;
        Assert.StartsWith("otpauth://totp/", setupJson.RootElement.GetProperty("otpAuthUri").GetString());

        var activationStep = Totp.CurrentStep(DateTimeOffset.UtcNow);
        // Sessao roubada nao basta: sem a senha, nem o codigo certo ativa.
        using (var noPassword = await admin.PostAsJsonAsync("/api/users/me/mfa/ativacao",
                   new { senha = "SenhaErrada@123", codigo = CodeAt(secret, activationStep) }))
            Assert.Equal(HttpStatusCode.BadRequest, noPassword.StatusCode);

        using var activation = await admin.PostAsJsonAsync("/api/users/me/mfa/ativacao",
            new { senha = TestPasswords.Valid, codigo = CodeAt(secret, activationStep) });
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        using var activationJson = await ReadJsonAsync(activation);
        var recoveryCodes = activationJson.RootElement.GetProperty("codigosRecuperacao").EnumerateArray()
            .Select(item => item.GetString()!).ToList();
        Assert.Equal(MfaService.RecoveryCodeCount, recoveryCodes.Count);
        // Sessoes abertas antes do MFA (sem segundo fator) caem com a SecurityVersion nova.
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/users/me/mfa")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var global = await db.UsuariosGlobais.SingleAsync(item => item.Email == AdminEmail);
            Assert.True(global.MfaHabilitado);
            Assert.Null(global.MfaSegredoPendenteProtegido);
            Assert.DoesNotContain(secret, global.MfaSegredoProtegido!);
            var hashes = await db.MfaCodigosRecuperacao.Where(item => item.UsuarioGlobalId == global.Id)
                .Select(item => item.CodigoHash).ToListAsync();
            Assert.All(recoveryCodes, code => Assert.DoesNotContain(hashes, hash => hash.Contains(code.Replace("-", ""))));
        }

        // Senha certa sem o segundo fator: so o desafio volta (sem token, cookie ou dado do usuario).
        using var anonymous = factory.CreateClient();
        var challenge = await LoginAsync(anonymous, Clinica.DefaultSlug);

        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(anonymous, Clinica.DefaultSlug, challenge, WrongCode(secret))).StatusCode);
        // O codigo usado na ativacao nao serve de novo (anti-replay).
        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(anonymous, Clinica.DefaultSlug, challenge, CodeAt(secret, activationStep))).StatusCode);
        using (var completed = await CompleteAsync(anonymous, Clinica.DefaultSlug, challenge, CodeAt(secret, activationStep + 1)))
        {
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            using var json = await ReadJsonAsync(completed);
            var token = json.RootElement.GetProperty("token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.True(completed.Headers.Contains("Set-Cookie"));
            admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // O mesmo codigo nao abre um segundo login (anti-replay entre desafios).
        var replay = await LoginAsync(anonymous, Clinica.DefaultSlug);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(anonymous, Clinica.DefaultSlug, replay, CodeAt(secret, activationStep + 1))).StatusCode);

        // Desafio e de uso unico, mesmo com outro fator valido.
        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(anonymous, Clinica.DefaultSlug, challenge, recoveryCodes[0])).StatusCode);

        var second = await LoginAsync(anonymous, Clinica.DefaultSlug);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(anonymous, Clinica.DefaultSlug, second, recoveryCodes[0])).StatusCode);
        var third = await LoginAsync(anonymous, Clinica.DefaultSlug);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(anonymous, Clinica.DefaultSlug, third, recoveryCodes[0])).StatusCode);

        using (var status = await ReadJsonAsync(await admin.GetAsync("/api/users/me/mfa")))
        {
            Assert.True(status.RootElement.GetProperty("habilitado").GetBoolean());
            Assert.Equal(MfaService.RecoveryCodeCount - 1, status.RootElement.GetProperty("codigosRecuperacaoRestantes").GetInt32());
        }

        using (var wrongPassword = await admin.PostAsJsonAsync("/api/users/me/mfa/desativacao",
                   new { senha = "SenhaErrada@123", codigo = recoveryCodes[1] }))
            Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        using (var disabled = await admin.PostAsJsonAsync("/api/users/me/mfa/desativacao",
                   new { senha = TestPasswords.Valid, codigo = recoveryCodes[1] }))
            Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);

        using var plain = await PostAsJsonWithClinicHeaderAsync(factory.CreateClient(), Clinica.DefaultSlug,
            "/api/users/authenticate", new { email = AdminEmail, senha = TestPasswords.Valid });
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        using var plainJson = await ReadJsonAsync(plain);
        Assert.False(string.IsNullOrWhiteSpace(plainJson.RootElement.GetProperty("token").GetString()));
        using var cleanup = factory.Services.CreateScope();
        var cleanDb = cleanup.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var cleared = await cleanDb.UsuariosGlobais.SingleAsync(item => item.Email == AdminEmail);
        Assert.Null(cleared.MfaSegredoProtegido);
        Assert.False(await cleanDb.MfaCodigosRecuperacao.AnyAsync(item => item.UsuarioGlobalId == cleared.Id));
    }

    [Fact]
    public async Task Challenge_IsLimitedToFiveAttempts()
    {
        using var factory = new HemodinksApiFactory();
        var secret = await EnableAsync(factory, AdminEmail);
        using var client = factory.CreateClient();
        var challenge = await LoginAsync(client, Clinica.DefaultSlug);
        for (var attempt = 0; attempt < MfaService.MaxChallengeAttempts; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(client, Clinica.DefaultSlug, challenge, WrongCode(secret))).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var desafio = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().MfaLoginDesafios
                .IgnoreQueryFilters().SingleAsync();
            Assert.Equal(MfaService.MaxChallengeAttempts, desafio.Tentativas);
            Assert.Null(desafio.UtilizadoEm);
        }

        // Esgotado: nem o codigo certo abre sessao (o limite por desafio do filtro tambem barra).
        using var exhausted = await CompleteAsync(client, Clinica.DefaultSlug, challenge, Code(secret, 0));
        Assert.Contains(exhausted.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests });
    }

    [Fact]
    public async Task Challenge_FromClinicA_IsRejectedInClinicB_WithoutBeingConsumed()
    {
        using var factory = new HemodinksApiFactory();
        var beta = await SeedClinicaBetaAsync(factory);
        var secret = await EnableAsync(factory, AdminEmail);
        using var client = factory.CreateClient();
        var challenge = await LoginAsync(client, Clinica.DefaultSlug);

        using (var crossClinic = await CompleteAsync(client, beta.Slug, challenge, Code(secret, 0)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, crossClinic.StatusCode);
            Assert.DoesNotContain("token", await crossClinic.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        using var sameClinic = await CompleteAsync(client, Clinica.DefaultSlug, challenge, Code(secret, 0));
        Assert.Equal(HttpStatusCode.OK, sameClinic.StatusCode);
        using var json = await ReadJsonAsync(sameClinic);
        Assert.Equal(1, json.RootElement.GetProperty("clinicaId").GetInt32());
    }

    [Fact]
    public async Task NonAdministrators_CannotConfigureOrResetMfa()
    {
        using var factory = new HemodinksApiFactory();
        var beta = await SeedClinicaBetaAsync(factory);
        using var doctor = factory.CreateClient();
        await AuthenticateAsync(doctor, beta.Slug, "dra.beta@hemodinks.com");

        using (var status = await ReadJsonAsync(await doctor.GetAsync("/api/users/me/mfa")))
            Assert.False(status.RootElement.GetProperty("elegivel").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.PostAsync("/api/users/me/mfa/configuracao", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.PostAsJsonAsync("/api/users/me/mfa/ativacao", new { codigo = "123456" })).StatusCode);

        await AddUserAsync(factory, beta.Id, "admin.beta@hemodinks.test", Perfil.AdministradorId);
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, beta.Slug, "admin.beta@hemodinks.test");
        using (var status = await ReadJsonAsync(await admin.GetAsync("/api/users/me/mfa")))
            Assert.True(status.RootElement.GetProperty("elegivel").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync("/api/users/1/mfa/redefinicao", null)).StatusCode);
    }

    [Fact]
    public async Task SuperAdministrator_ResetsMfaOnlyInsideOwnClinic_AndEndsTheHolderSessions()
    {
        const string targetEmail = "admin.alpha@hemodinks.test";
        using var factory = new HemodinksApiFactory();
        var beta = await SeedClinicaBetaAsync(factory);
        await AddUserAsync(factory, 1, targetEmail, Perfil.AdministradorId);
        await EnableAsync(factory, targetEmail);
        await EnableAsync(factory, AdminEmail);
        int targetUserId;
        Guid previousVersion;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var member = await db.UsuariosClinicas.Include(item => item.UsuarioGlobal)
                .SingleAsync(item => item.ClinicaId == 1 && item.UsuarioGlobal.Email == targetEmail);
            targetUserId = member.UserId;
            previousVersion = member.UsuarioGlobal.SecurityVersion;
        }

        await AddUserAsync(factory, 1, "sa.alpha@hemodinks.test", Perfil.SuperAdministradorId);
        await AddUserAsync(factory, beta.Id, "sa.beta@hemodinks.test", Perfil.SuperAdministradorId);

        using var betaSuper = factory.CreateClient();
        await AuthenticateAsync(betaSuper, beta.Slug, "sa.beta@hemodinks.test");
        Assert.Equal(HttpStatusCode.NotFound, (await betaSuper.PostAsync($"/api/users/{targetUserId}/mfa/redefinicao", null)).StatusCode);
        using (var unchanged = factory.Services.CreateScope())
            Assert.True((await unchanged.ServiceProvider.GetRequiredService<PlatformDbContext>().UsuariosGlobais
                .SingleAsync(item => item.Email == targetEmail)).MfaHabilitado);

        using var alphaSuper = factory.CreateClient();
        await AuthenticateAsync(alphaSuper, Clinica.DefaultSlug, "sa.alpha@hemodinks.test");
        int otherSuperId, selfId;
        using (var ids = factory.Services.CreateScope())
        {
            var users = ids.ServiceProvider.GetRequiredService<PlatformDbContext>().Users.IgnoreQueryFilters();
            otherSuperId = (await users.SingleAsync(item => item.ClinicaId == 1 && item.Email == AdminEmail)).Id;
            selfId = (await users.SingleAsync(item => item.ClinicaId == 1 && item.Email == "sa.alpha@hemodinks.test")).Id;
        }

        // Outro SuperAdministrador (ou o proprio) so desativa com senha e codigo.
        Assert.Equal(HttpStatusCode.Forbidden, (await alphaSuper.PostAsync($"/api/users/{otherSuperId}/mfa/redefinicao", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alphaSuper.PostAsync($"/api/users/{selfId}/mfa/redefinicao", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alphaSuper.PostAsync($"/api/users/{targetUserId}/mfa/redefinicao", null)).StatusCode);

        using var verify = factory.Services.CreateScope();
        var reset = await verify.ServiceProvider.GetRequiredService<PlatformDbContext>().UsuariosGlobais
            .SingleAsync(item => item.Email == targetEmail);
        Assert.False(reset.MfaHabilitado);
        Assert.Null(reset.MfaSegredoProtegido);
        Assert.NotEqual(previousVersion, reset.SecurityVersion);
        Assert.True((await verify.ServiceProvider.GetRequiredService<PlatformDbContext>().UsuariosGlobais
            .SingleAsync(item => item.Email == AdminEmail)).MfaHabilitado);
        using var plain = factory.CreateClient();
        await AuthenticateAsync(plain, Clinica.DefaultSlug, targetEmail);
    }

    private static string Code(string secret, int offset) =>
        CodeAt(secret, Totp.CurrentStep(DateTimeOffset.UtcNow) + offset);

    private static string CodeAt(string secret, long step) => Totp.ComputeCode(Base32.Decode(secret), step);

    // Codigo fora da janela de +-1 passo: nunca valido agora.
    private static string WrongCode(string secret)
    {
        var step = Totp.CurrentStep(DateTimeOffset.UtcNow);
        var valid = new[] { -1, 0, 1, 2 }.Select(offset => Totp.ComputeCode(Base32.Decode(secret), step + offset)).ToHashSet();
        return Enumerable.Range(0, 10).Select(seed => (seed * 111111).ToString("D6")).First(code => !valid.Contains(code));
    }

    private static async Task<string> LoginAsync(HttpClient client, string slug)
    {
        using var response = await PostAsJsonWithClinicHeaderAsync(client, slug, "/api/users/authenticate",
            new { email = AdminEmail, senha = TestPasswords.Valid });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var json = await ReadJsonAsync(response);
        var properties = json.RootElement.EnumerateObject().Select(item => item.Name).ToList();
        Assert.Equal(["mfaDesafio"], properties);
        return json.RootElement.GetProperty("mfaDesafio").GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, string slug, string challenge, string code) =>
        PostAsJsonWithClinicHeaderAsync(client, slug, "/api/users/authenticate/mfa", new { desafio = challenge, codigo = code });

    private static async Task AddUserAsync(HemodinksApiFactory factory, int clinicaId, string email, int perfilId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var hash = new PasswordHasher().HashPassword(TestPasswords.Valid);
        var global = new UsuarioGlobal { Nome = "Usuario MFA", Email = email, Senha = hash, SecurityVersion = Guid.NewGuid() };
        var user = new User { ClinicaId = clinicaId, Nome = "Usuario MFA", Email = email, Telefone = "11987654321",
            Senha = hash, PerfilId = perfilId, Ativo = true };
        db.UsuariosClinicas.Add(new UsuarioClinica { ClinicaId = clinicaId, User = user, UsuarioGlobal = global, PerfilId = perfilId });
        await db.SaveChangesAsync();
    }

    // Ativa direto no banco (segredo protegido pelo mesmo protetor da API) para nao gastar o limite de taxa.
    private static async Task<string> EnableAsync(HemodinksApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IMfaSecretProtector>();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var secret = Totp.GenerateSecret();
        var global = await db.UsuariosGlobais.SingleAsync(item => item.Email == email);
        global.MfaHabilitado = true;
        global.MfaSegredoProtegido = protector.Protect(secret);
        global.MfaHabilitadoEm = DateTime.UtcNow;
        global.MfaUltimoPassoUsado = Totp.CurrentStep(DateTimeOffset.UtcNow) - 5;
        await db.SaveChangesAsync();
        return secret;
    }
}
