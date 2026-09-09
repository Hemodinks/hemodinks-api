using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Validation;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class TemporaryAccessTests
{
    private static readonly PasswordHasher Hasher = new();
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static async Task<User> AddUser(AppDbContext db, int id, int profile = Perfil.AdministradorId, int clinic = 1)
    {
        var user = new User { Id = id, ClinicaId = clinic, Nome = "Teste", Email = $"test{id}@example.com", Telefone = "11999999999",
            Senha = Hasher.HashPassword(TestPasswords.Valid), PerfilId = profile, Ativo = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
    private static CurrentUserContext Actor(User user) => new(user.Id, user.PerfilId, user.Nome, user.ClinicaId);

    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.SuperAdministradorId)]
    public async Task AuthorizedAdministrator_GeneratesOnlyHashAndAudits(int profile)
    {
        await using var db = TestDbContextFactory.Create();
        var actor = await AddUser(db, 100, profile);
        var target = await AddUser(db, 101, Perfil.MedicosId);
        var clock = new Clock();
        var response = await new TemporaryAccessService(db, Hasher, clock).GenerateAsync(target.Id, Actor(actor), default);
        var stored = await db.TemporaryAccessCredentials.SingleAsync();
        Assert.Equal(clock.Now.UtcDateTime.AddMinutes(5), stored.ExpiresAtUtc);
        Assert.True(Hasher.VerifyPassword(response.SenhaTemporaria!, stored.PasswordHash));
        Assert.True(Hasher.VerifyPassword(TestPasswords.Valid, target.Senha));
        Assert.DoesNotContain(response.SenhaTemporaria!, System.Text.Json.JsonSerializer.Serialize(db.AuditoriasPlataforma.Select(x => new { x.Acao, x.Recurso, x.EntidadeId, x.DetalhesJson }).ToList()));
        var audit = await db.AuditoriasPlataforma.SingleAsync();
        Assert.Equal(actor.Id, audit.UserId);
        Assert.Equal(target.Id.ToString(), audit.EntidadeId);
        Assert.True(audit.Sucesso);
    }

    [Theory]
    [InlineData(Perfil.MedicosId, Perfil.MedicosId, 1)]
    [InlineData(Perfil.AdministradorId, Perfil.SuperAdministradorId, 1)]
    [InlineData(Perfil.AdministradorId, Perfil.MedicosId, 2)]
    public async Task UnauthorizedGeneration_IsDenied(int actorProfile, int targetProfile, int tenant)
    {
        await using var db = TestDbContextFactory.Create();
        var actor = await AddUser(db, 100, actorProfile);
        var target = await AddUser(db, 101, targetProfile);
        var caller = Actor(actor) with { ClinicaId = tenant };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new TemporaryAccessService(db, Hasher, new Clock()).GenerateAsync(target.Id, caller, default));
        Assert.Empty(db.TemporaryAccessCredentials);
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(301, false)]
    public async Task Credential_UsesBackendClockAndExpiresAtBoundary(int seconds, bool valid)
    {
        await using var db = TestDbContextFactory.Create();
        var user = await AddUser(db, 100);
        var clock = new Clock();
        var service = new TemporaryAccessService(db, Hasher, clock);
        var password = await service.GenerateAsync(user.Id, Actor(user), default);
        clock.Now = clock.Now.AddSeconds(seconds);
        var membership = await db.UsuariosClinicas.Include(x => x.UsuarioGlobal).SingleAsync();
        var login = await service.AuthenticateAsync(user, membership, password.SenhaTemporaria!, default);
        Assert.Equal(valid, login != null);
        if (valid)
        {
            Assert.True(login!.UsuarioGlobal.TemporaryPasswordRecovery);
            Assert.Null(await service.AuthenticateAsync(user, membership, password.SenhaTemporaria!, default));
            Assert.Contains(db.AuditoriasPlataforma, x => x.Acao == "TemporaryPassword.Used");
        }
    }

    [Fact]
    public async Task Regeneration_RevokesPreviousPasswordAndSessions()
    {
        await using var db = TestDbContextFactory.Create();
        var user = await AddUser(db, 100);
        var clock = new Clock();
        var service = new TemporaryAccessService(db, Hasher, clock);
        var first = await service.GenerateAsync(user.Id, Actor(user), default);
        var membership = await db.UsuariosClinicas.Include(x => x.UsuarioGlobal).SingleAsync();
        var version = membership.UsuarioGlobal.SecurityVersion;
        db.AuthenticationSessions.Add(new AuthenticationSession { Id = Guid.NewGuid(), UsuarioGlobalId = membership.UsuarioGlobalId,
            UsuarioClinicaId = membership.Id, RefreshTokenHash = "hash", SecurityVersion = version });
        await db.SaveChangesAsync();
        clock.Now = clock.Now.AddSeconds(31);
        var second = await service.GenerateAsync(user.Id, Actor(user), default);
        Assert.Single(db.TemporaryAccessCredentials);
        Assert.NotEqual(version, membership.UsuarioGlobal.SecurityVersion);
        Assert.NotNull((await db.AuthenticationSessions.SingleAsync()).RevokedAt);
        Assert.Null(await service.AuthenticateAsync(user, membership, first.SenhaTemporaria!, default));
        Assert.NotNull(await service.AuthenticateAsync(user, membership, second.SenhaTemporaria!, default));
    }

    [Fact]
    public async Task RevokedCredential_AndOtherAccountCannotAuthenticate()
    {
        await using var db = TestDbContextFactory.Create();
        var user = await AddUser(db, 100);
        var other = await AddUser(db, 101);
        var service = new TemporaryAccessService(db, Hasher, new Clock());
        var response = await service.GenerateAsync(user.Id, Actor(user), default);
        var membership = await db.UsuariosClinicas.Include(x => x.UsuarioGlobal).SingleAsync();
        var wrongScope = new User { Id = user.Id, ClinicaId = 2 };
        Assert.Null(await service.AuthenticateAsync(wrongScope, membership, response.SenhaTemporaria!, default));
        var credential = await db.TemporaryAccessCredentials.SingleAsync();
        credential.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.Null(await service.AuthenticateAsync(user, membership, response.SenhaTemporaria!, default));
    }

    [Fact]
    public async Task Completion_RequiresConsumptionAndVersion_ThenRestoresPersonalLogin()
    {
        await using var db = TestDbContextFactory.Create();
        var user = await AddUser(db, 100);
        var service = new TemporaryAccessService(db, Hasher, new Clock());
        var generated = await service.GenerateAsync(user.Id, Actor(user), default);
        var membership = await db.UsuariosClinicas.Include(x => x.UsuarioGlobal).SingleAsync();
        var command = new ChangeTemporaryPasswordCommand { CurrentUser = Actor(user), SecurityVersion = membership.UsuarioGlobal.SecurityVersion,
            NovaSenha = "NovaSenha@456", Confirmacao = "NovaSenha@456" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CompleteAsync(command, default));
        await service.AuthenticateAsync(user, membership, generated.SenhaTemporaria!, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CompleteAsync(command, default));
        command.SecurityVersion = membership.UsuarioGlobal.SecurityVersion;
        var result = await service.CompleteAsync(command, default);
        Assert.False(result.PrecisaTrocarSenha);
        Assert.False(membership.UsuarioGlobal.TemporaryPasswordRecovery);
        Assert.NotNull(await GlobalIdentityService.AuthenticateAsync(db, Hasher, user, command.NovaSenha, default));
        Assert.Null(await GlobalIdentityService.AuthenticateAsync(db, Hasher, user, generated.SenhaTemporaria!, default));
        Assert.Null(await service.AuthenticateAsync(user, membership, generated.SenhaTemporaria!, default));
        Assert.Contains(db.AuditoriasPlataforma, x => x.Acao == "TemporaryPassword.Completed");
    }

    [Theory]
    [InlineData("short", "short")]
    [InlineData("NovaSenha@123", "OutraSenha@123")]
    public void FluentValidator_RejectsInvalidPassword(string password, string confirmation)
    {
        IRequestValidator<ChangeTemporaryPasswordCommand> validator = new ChangeTemporaryPasswordCommandValidator();
        Assert.Throws<InvalidOperationException>(() => validator.Validate(new() { NovaSenha = password, Confirmacao = confirmation }));
    }

    [Fact]
    public async Task OtherTenantTarget_IsNotFoundEvenInPlatformContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new PlatformDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var actor = await AddUser(db, 100);
        var target = await AddUser(db, 101, Perfil.MedicosId, 2);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new TemporaryAccessService(db, Hasher, new Clock()).GenerateAsync(target.Id, Actor(actor), default));
        Assert.Empty(db.TemporaryAccessCredentials);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HigherPrivilegeInAnotherClinic_PreventsGlobalCredentialTakeover(bool linked)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new PlatformDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var actor = await AddUser(db, 100);
        var target = await AddUser(db, 101, Perfil.MedicosId);
        var higher = await AddUser(db, 102, Perfil.SuperAdministradorId, 2);
        higher.Email = target.Email;
        var membership = await GlobalIdentityService.EnsureForUserAsync(db, target, default);
        if (linked) db.UsuariosClinicas.Add(new UsuarioClinica { UsuarioGlobalId = membership.UsuarioGlobalId, UserId = higher.Id,
            ClinicaId = 2, PerfilId = Perfil.SuperAdministradorId, Ativo = true });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new TemporaryAccessService(db, Hasher, new Clock()).GenerateAsync(target.Id, Actor(actor), default));
        Assert.Empty(db.TemporaryAccessCredentials);
        Assert.Contains(db.AuditoriasPlataforma, x => x.Acao == "TemporaryPassword.GenerationDenied" && !x.Sucesso);
    }

    [Fact]
    public async Task ImmediateRegeneration_IsThrottledWithoutReplacingCredential()
    {
        await using var db = TestDbContextFactory.Create();
        var user = await AddUser(db, 100);
        var service = new TemporaryAccessService(db, Hasher, new Clock());
        var response = await service.GenerateAsync(user.Id, Actor(user), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(user.Id, Actor(user), default));
        Assert.True(Hasher.VerifyPassword(response.SenhaTemporaria!, (await db.TemporaryAccessCredentials.SingleAsync()).PasswordHash));
    }
}
