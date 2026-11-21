using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

/// <summary>
/// #363: troca e redefinicao de senha entre clinicas. Os servicos de identidade rodam no
/// PlatformDbContext (sem filtro de tenant), entao o isolamento tem de vir das regras explicitas.
/// A confirmacao por token entre identidades distintas ja e coberta por
/// PasswordResetSecurityEndpointTests.TokenRecovery_RevokesSameIdentityAcrossClinics_WithoutChangingOtherUsersOrMemberships
/// e a senha temporaria gerada por administrador por TemporaryAccessTests.
/// </summary>
public sealed class CrossClinicPasswordTests
{
    private const int ClinicA = Clinica.DefaultId;
    private const int ClinicB = 9002;
    private const string NewPassword = "OutraSenha@Forte123";
    private static readonly PasswordHasher Hasher = new();

    [Fact]
    public async Task ChangePassword_SessionOpenedInClinicA_CannotActAsSameIdentityMembershipInClinicB()
    {
        await using var db = TestDbContextFactory.CreatePlatform();
        await EnsureClinicsAsync(db);
        var (userA, membershipA) = await AddMemberAsync(db, 301, ClinicA, "pessoa@example.com");
        var (userB, membershipB) = await AddMemberAsync(db, 302, ClinicB, "pessoa@example.com");
        Assert.Equal(membershipA.UsuarioGlobalId, membershipB.UsuarioGlobalId);
        var session = await OpenSessionAsync(db, membershipA);
        var before = await SnapshotAsync(db);

        // Sessao real da Clinica A com o contexto alegado da Clinica B (cabecalho trocado ou token adulterado).
        var actor = new CurrentUserContext(userB.Id, userB.PerfilId, "Test", ClinicB,
            UsuarioGlobalId: membershipB.UsuarioGlobalId, UsuarioClinicaId: membershipB.Id) { SessionId = session.Id };

        await AssertDeniedAsync(db, actor, userB.Id);
        Assert.Equal(before, await SnapshotAsync(db));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangePassword_UserOfClinicB_CannotUseSessionOfAnotherIdentityInClinicA(bool claimsVictimIdentity)
    {
        await using var db = TestDbContextFactory.CreatePlatform();
        await EnsureClinicsAsync(db);
        var (_, membershipA) = await AddMemberAsync(db, 311, ClinicA, "vitima@example.com");
        var (userB, membershipB) = await AddMemberAsync(db, 312, ClinicB, "atacante@example.com");
        Assert.NotEqual(membershipA.UsuarioGlobalId, membershipB.UsuarioGlobalId);
        var victimSession = await OpenSessionAsync(db, membershipA);
        var before = await SnapshotAsync(db);

        // O atacante conhece o Id da sessao da vitima. Ou mantem a propria identidade, ou copia a
        // identidade global e o vinculo da vitima, trocando so o proprio Id de usuario.
        var actor = claimsVictimIdentity
            ? new CurrentUserContext(userB.Id, userB.PerfilId, "Test", ClinicA,
                UsuarioGlobalId: membershipA.UsuarioGlobalId, UsuarioClinicaId: membershipA.Id) { SessionId = victimSession.Id }
            : new CurrentUserContext(userB.Id, userB.PerfilId, "Test", ClinicB,
                UsuarioGlobalId: membershipB.UsuarioGlobalId, UsuarioClinicaId: membershipB.Id) { SessionId = victimSession.Id };

        await AssertDeniedAsync(db, actor, userB.Id);
        Assert.Equal(before, await SnapshotAsync(db));
    }

    [Fact]
    public async Task ChangePassword_LegitimateHolder_SucceedsWithSameFixture()
    {
        // Controle positivo: as negativas acima vem das regras, nao de um setup em que nada funcionaria.
        await using var db = TestDbContextFactory.CreatePlatform();
        await EnsureClinicsAsync(db);
        var (userA, membershipA) = await AddMemberAsync(db, 331, ClinicA, "titular@example.com");
        await AddMemberAsync(db, 332, ClinicB, "titular@example.com");
        var session = await OpenSessionAsync(db, membershipA);
        var handler = new ChangePasswordCommandHandler(TestPasswordPolicy.Instance, SensitiveIdentityTestSupport.Service(db));

        await handler.Handle(new ChangePasswordCommand
        {
            UserId = userA.Id, CurrentUser = SensitiveIdentityTestSupport.Actor(session),
            SenhaAtual = TestPasswords.Valid, NovaSenha = NewPassword
        }, CancellationToken.None);

        var global = await db.UsuariosGlobais.AsNoTracking().SingleAsync(x => x.Id == membershipA.UsuarioGlobalId);
        Assert.True(Hasher.VerifyPassword(NewPassword, global.Senha));
        Assert.NotNull((await db.AuthenticationSessions.AsNoTracking().SingleAsync(x => x.Id == session.Id)).RevokedAt);
    }

    [Fact]
    public async Task PasswordResetRequest_FromClinicB_DoesNotReachAccountThatExistsOnlyInClinicA()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var platform = new PlatformDbContext(options, TestCpfProtection.Instance))
        {
            await platform.Database.EnsureCreatedAsync();
            await EnsureClinicsAsync(platform);
            await AddMemberAsync(platform, 321, ClinicA, "so-na-a@example.com");
        }

        // Em producao o pedido roda no AppDbContext da requisicao (IPasswordResetOperationsDbContext).
        var fromB = await RequestResetAsync(options, ClinicB, "clinica-b", "so-na-a@example.com");
        Assert.Equal(PasswordResetRules.CreateRequestResponse().Message, fromB.Response.Message);
        Assert.Empty(fromB.Sender.Notifications);
        await using (var platform = new PlatformDbContext(options, TestCpfProtection.Instance))
            Assert.Empty(await platform.PasswordResetTokens.ToListAsync());

        // Controle positivo: o mesmo pedido na clinica dona cria o token, entao a negativa vem do escopo.
        var fromA = await RequestResetAsync(options, ClinicA, "clinica-a", "so-na-a@example.com");
        Assert.Equal(fromB.Response.Message, fromA.Response.Message);
        var notification = Assert.Single(fromA.Sender.Notifications);
        Assert.Equal(ClinicA, notification.ClinicaId);
        await using (var platform = new PlatformDbContext(options, TestCpfProtection.Instance))
            Assert.Equal(ClinicA, Assert.Single(await platform.PasswordResetTokens.ToListAsync()).ClinicaId);
    }

    private static async Task AssertDeniedAsync(PlatformDbContext db, CurrentUserContext actor, int userId)
    {
        var handler = new ChangePasswordCommandHandler(TestPasswordPolicy.Instance, SensitiveIdentityTestSupport.Service(db));
        var error = await Assert.ThrowsAsync<SensitiveIdentityException>(() => handler.Handle(new ChangePasswordCommand
        {
            UserId = userId, CurrentUser = actor, SenhaAtual = TestPasswords.Valid, NovaSenha = NewPassword
        }, CancellationToken.None));
        Assert.Equal("identity_revalidation_failed", error.Code);
    }

    private static async Task<(RecordingPasswordResetNotificationSender Sender, RequestPasswordResetResponse Response)> RequestResetAsync(
        DbContextOptions<AppDbContext> options, int clinicId, string slug, string email)
    {
        var tenant = new ClinicaContext();
        tenant.SetCurrent(clinicId, slug);
        await using var db = new AppDbContext(options, tenant, TestCpfProtection.Instance);
        var sender = new RecordingPasswordResetNotificationSender();
        var response = await new ResetUserPasswordByEmailCommandHandler(db, sender,
            NullLogger<ResetUserPasswordByEmailCommandHandler>.Instance).Handle(new ResetUserPasswordByEmailCommand { Email = email },
            CancellationToken.None);
        return (sender, response);
    }

    private static async Task EnsureClinicsAsync(AppDbContext db)
    {
        foreach (var (id, slug) in new[] { (ClinicA, "clinica-a"), (ClinicB, "clinica-b") })
        {
            if (await db.Clinicas.FindAsync(id) == null)
                db.Clinicas.Add(new Clinica { Id = id, Nome = slug, Slug = slug, Ativa = true });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<(User User, UsuarioClinica Membership)> AddMemberAsync(
        PlatformDbContext db, int id, int clinicId, string email)
    {
        var user = new User
        {
            Id = id, ClinicaId = clinicId, Nome = email, Email = email, Telefone = "11999999999",
            Senha = Hasher.HashPassword(TestPasswords.Valid), PerfilId = Perfil.AdministradorId, Ativo = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user, await GlobalIdentityService.EnsureForUserAsync(db, user, CancellationToken.None));
    }

    private static async Task<AuthenticationSession> OpenSessionAsync(PlatformDbContext db, UsuarioClinica membership)
    {
        var session = new AuthenticationSession
        {
            Id = Guid.NewGuid(), UsuarioGlobalId = membership.UsuarioGlobalId, UsuarioClinicaId = membership.Id,
            SecurityVersion = membership.UsuarioGlobal.SecurityVersion, RefreshTokenHash = Guid.NewGuid().ToString()
        };
        db.AuthenticationSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    // Senhas e versoes de todas as identidades e usuarios, mais sessoes revogadas: nada pode mudar.
    private static async Task<string> SnapshotAsync(PlatformDbContext db)
    {
        var globals = await db.UsuariosGlobais.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => $"G{x.Id}:{x.Senha}:{x.SecurityVersion}").ToListAsync();
        var users = await db.Users.AsNoTracking().OrderBy(x => x.Id).Select(x => $"U{x.Id}:{x.Senha}").ToListAsync();
        var sessions = await db.AuthenticationSessions.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => $"S{x.Id}:{x.RevokedAt}").ToListAsync();
        return string.Join("|", globals.Concat(users).Concat(sessions));
    }
}
