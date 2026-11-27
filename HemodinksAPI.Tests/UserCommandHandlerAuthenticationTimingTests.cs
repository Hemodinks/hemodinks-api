using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class UserCommandHandlerTests
{
    // #432: toda falha do /authenticate custa o mesmo que uma senha errada (uma verificacao PBKDF2).
    [Theory]
    [InlineData("wrong-password")]
    [InlineData("unknown-email")]
    [InlineData("locked")]
    [InlineData("inactive-membership")]
    [InlineData("inactive-global")]
    [InlineData("missing-team")]
    [InlineData("recovery-without-credential")]
    [InlineData("recovery-wrong-password")]
    [InlineData("recovery-expired")]
    [InlineData("inactive-user")]
    [InlineData("inactive-clinic")]
    public async Task Authenticate_EveryFailurePath_PerformsExactlyOnePasswordVerification(string scenario)
    {
        await using var context = TestDbContextFactory.CreatePlatform();
        var stored = new PasswordHasher();
        var user = CreateUser("timing-432@example.invalid", stored.HashPassword(TestPasswords.Valid),
            perfilId: scenario == "missing-team" ? Perfil.EquipeId : Perfil.MedicosId);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var member = await GlobalIdentityService.EnsureForUserAsync(context, user, default);
        if (scenario == "inactive-membership") member.Ativo = false;
        if (scenario == "inactive-global") member.UsuarioGlobal.Ativo = false;
        if (scenario.StartsWith("recovery", StringComparison.Ordinal)) member.UsuarioGlobal.TemporaryPasswordRecovery = true;
        if (scenario is "recovery-wrong-password" or "recovery-expired")
        {
            context.TemporaryAccessCredentials.Add(new TemporaryAccessCredential
            {
                UsuarioGlobalId = member.UsuarioGlobalId, UserId = user.Id, ClinicaId = user.ClinicaId,
                PasswordHash = stored.HashPassword("Temporaria@432"),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(scenario == "recovery-expired" ? -5 : 5)
            });
        }
        if (scenario == "inactive-user") user.Ativo = false;
        if (scenario == "inactive-clinic") (await context.Clinicas.SingleAsync(item => item.Id == user.ClinicaId)).Ativa = false;
        await context.SaveChangesAsync();

        var hasher = new TimingCountingHasher();
        // O servico de acesso temporario tem o proprio hasher: a contagem tem de vir do verificador do handler.
        var temporaryHasher = new TimingCountingHasher();
        var handler = new AuthenticateUserCommandHandler(context, hasher, new StubJwtTokenService("token"),
            CreateLicencaService(context), ClinicaContextFactory.CreateDefaultResolved(),
            new TimingTestProtection(scenario == "locked"), NullLogger<AuthenticateUserCommandHandler>.Instance,
            new TemporaryAccessService(TestPasswordPolicy.Instance, context, temporaryHasher, TimeProvider.System));

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new AuthenticateUserCommand
        {
            Email = scenario == "unknown-email" ? "unknown-432@example.invalid" : user.Email,
            Senha = scenario switch
            {
                "wrong-password" or "recovery-wrong-password" => "senha-errada",
                "recovery-expired" => "Temporaria@432",
                _ => TestPasswords.Valid
            }
        }, default));

        Assert.Equal("Email ou senha invalidos", error.Message);
        Assert.Equal(1, hasher.Verifications);
        Assert.Equal(0, temporaryHasher.Verifications);
    }

    // Tenant (AGENTS.md): usuario da Clinica A autenticando no contexto da Clinica B e negado como conta
    // inexistente (mesma mensagem, mesmo custo), sem revelar que o email existe em outra clinica.
    [Fact]
    public async Task Authenticate_UserFromClinicA_InClinicBContext_IsDeniedLikeUnknownAccount()
    {
        var options = new DbContextOptionsBuilder<HemodinksAPI.Infrastructure.Data.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        const int clinicB = 432;
        await using (var seed = new HemodinksAPI.Infrastructure.Data.PlatformDbContext(options, TestCpfProtection.Instance))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Clinicas.Add(new Clinica { Id = clinicB, Nome = "Clinica B", Slug = "clinica-b-432" });
            var userA = CreateUser("clinica-a-432@example.invalid", new PasswordHasher().HashPassword(TestPasswords.Valid));
            userA.ClinicaId = Clinica.DefaultId;
            seed.Users.Add(userA);
            await seed.SaveChangesAsync();
            await GlobalIdentityService.EnsureForUserAsync(seed, userA, default);
        }

        var tenantB = new ClinicaContext();
        tenantB.SetCurrent(clinicB, "clinica-b-432");
        await using var context = new HemodinksAPI.Infrastructure.Data.AppDbContext(options, tenantB, TestCpfProtection.Instance);
        var hasher = new TimingCountingHasher();
        var handler = new AuthenticateUserCommandHandler(context, hasher, new StubJwtTokenService("token"),
            CreateLicencaService(context), tenantB, new TimingTestProtection(false),
            NullLogger<AuthenticateUserCommandHandler>.Instance);

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(
            new AuthenticateUserCommand { Email = "clinica-a-432@example.invalid", Senha = TestPasswords.Valid }, default));

        Assert.Equal("Email ou senha invalidos", error.Message);
        Assert.Equal(1, hasher.Verifications);
    }

    [Fact]
    public async Task Authenticate_ValidCredential_IsNotSlowedByReferenceVerification()
    {
        await using var context = TestDbContextFactory.CreatePlatform();
        var user = CreateUser("timing-ok-432@example.invalid", new PasswordHasher().HashPassword(TestPasswords.Valid));
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var hasher = new TimingCountingHasher();
        var handler = new AuthenticateUserCommandHandler(context, hasher, new StubJwtTokenService("token"),
            CreateLicencaService(context), ClinicaContextFactory.CreateDefaultResolved(),
            new TimingTestProtection(false), NullLogger<AuthenticateUserCommandHandler>.Instance);

        var response = await handler.Handle(new AuthenticateUserCommand { Email = user.Email, Senha = TestPasswords.Valid }, default);

        Assert.Equal("token", response.Token);
        Assert.Equal(1, hasher.Verifications);
    }

    [Fact]
    public void VerificationTracker_EqualizesOnlyWhenNothingWasVerified()
    {
        var inner = new TimingCountingHasher();
        var tracker = new PasswordVerificationTracker(inner);
        tracker.EqualizeIfNothingVerified(TestPasswords.Valid);
        Assert.Equal(1, inner.Verifications);

        tracker.VerifyPassword(TestPasswords.Valid, new PasswordHasher().HashPassword("outra-senha"));
        tracker.EqualizeIfNothingVerified(TestPasswords.Valid);
        Assert.Equal(2, inner.Verifications);
        Assert.Equal(1, tracker.Verifications);
    }

    private sealed class TimingCountingHasher : IPasswordHasher
    {
        private readonly PasswordHasher _inner = new();
        public int Verifications { get; private set; }
        public string HashPassword(string password) => _inner.HashPassword(password);
        public bool VerifyPassword(string password, string hash) { Verifications++; return _inner.VerifyPassword(password, hash); }
        public PasswordVerificationResult VerifyPasswordWithRehash(string password, string hash)
        {
            Verifications++;
            return _inner.VerifyPasswordWithRehash(password, hash);
        }
    }

    private sealed class TimingTestProtection(bool locked) : ILoginAccountProtection
    {
        public Task<bool> IsLockedAsync(int id, CancellationToken ct) => Task.FromResult(locked);
        public Task RegisterFailureAsync(int id, CancellationToken ct) => Task.CompletedTask;
        public Task RegisterSuccessAsync(int id, CancellationToken ct) => Task.CompletedTask;
    }
}
