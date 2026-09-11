using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Utils;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class LoginContextWorkTests
{
    [Fact]
    public async Task SharedIdentity_VerifiesHashAndChecksLockOncePerRequest()
    {
        await using var context = await SeedAsync();
        var hasher = new CountingHasher();
        var protection = new RecordingProtection();
        var handler = Handler(context, hasher, protection);
        var request = new ResolveLoginClinicsCommand { Email = "SHARED@example.com", Senha = TestPasswords.Valid };

        var response = await handler.Handle(request, default);
        Assert.Equal(3, response.Clinicas.Count);
        Assert.Equal(1, hasher.Verifications);
        Assert.Equal(1, protection.Checks);

        // Neither successful credentials nor lock decisions survive a request.
        protection.Locked = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(request, default));
        Assert.Equal(2, protection.Checks);
        Assert.Equal(1, protection.Failures);
        protection.Locked = false;
        request.Senha = "incorrect-password";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(request, default));
        Assert.Equal(2, hasher.Verifications);
        Assert.Equal(3, protection.Checks);
        Assert.Equal(2, protection.Failures);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("expired")]
    [InlineData("used")]
    [InlineData("revoked")]
    public async Task TemporaryCredential_OnlyResolvesItsExactClinicWhileUsable(string state)
    {
        await using var context = await SeedAsync();
        var global = await context.UsuariosGlobais.SingleAsync(item => item.Email == "shared@example.com");
        global.TemporaryPasswordRecovery = true;
        context.TemporaryAccessCredentials.Add(new TemporaryAccessCredential
        {
            UsuarioGlobalId = global.Id, UserId = 102, ClinicaId = 102,
            PasswordHash = new PasswordHasher().HashPassword(TestPasswords.Valid),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(state == "expired" ? -5 : 5),
            UsedAtUtc = state == "used" ? DateTime.UtcNow : null,
            RevokedAtUtc = state == "revoked" ? DateTime.UtcNow : null
        });
        await context.SaveChangesAsync();
        var handler = Handler(context, new CountingHasher(), new RecordingProtection());
        var request = new ResolveLoginClinicsCommand { Email = global.Email, Senha = TestPasswords.Valid };
        if (state == "valid")
        {
            var result = await handler.Handle(request, default);
            Assert.Equal(102, Assert.Single(result.Clinicas).ClinicaId);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(request, default));
        }
    }

    private static ResolveLoginClinicsCommandHandler Handler(PlatformDbContext context, IPasswordHasher hasher, ILoginAccountProtection protection) =>
        new(context, hasher, protection, TimeProvider.System, NullLogger<ResolveLoginClinicsCommandHandler>.Instance);

    private static async Task<PlatformDbContext> SeedAsync()
    {
        var context = new PlatformDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await context.Database.EnsureCreatedAsync();
        var global = new UsuarioGlobal { Nome = "Shared", Email = "shared@example.com",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid), DataAtualizacao = DateTime.UtcNow };
        for (var id = 101; id <= 103; id++)
        {
            var clinic = new Clinica { Id = id, Nome = $"Clinic {id}", Slug = $"clinic-{id}" };
            var user = new User { Id = id, Clinica = clinic, ClinicaId = id, Nome = "Shared", Email = global.Email,
                Senha = global.Senha, Telefone = "11999999999" };
            context.UsuariosClinicas.Add(new UsuarioClinica { UsuarioGlobal = global, User = user,
                UserId = id, Clinica = clinic, ClinicaId = id, PerfilId = Perfil.MedicosId });
        }
        await context.SaveChangesAsync();
        return context;
    }

    private sealed class CountingHasher : IPasswordHasher
    {
        private readonly PasswordHasher _inner = new();
        public int Verifications { get; private set; }
        public string HashPassword(string password) => _inner.HashPassword(password);
        public bool VerifyPassword(string password, string hash)
        {
            Verifications++;
            return _inner.VerifyPassword(password, hash);
        }
    }

    private sealed class RecordingProtection : ILoginAccountProtection
    {
        public bool Locked { get; set; }
        public int Checks { get; private set; }
        public int Failures { get; private set; }
        public Task<bool> IsLockedAsync(int id, CancellationToken ct) { Checks++; return Task.FromResult(Locked); }
        public Task RegisterFailureAsync(int id, CancellationToken ct) { Failures++; return Task.CompletedTask; }
        public Task RegisterSuccessAsync(int id, CancellationToken ct) => Task.CompletedTask;
    }
}
