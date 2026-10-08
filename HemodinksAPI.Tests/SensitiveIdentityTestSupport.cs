using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

internal sealed class EmailConfirmationRecorder : IEmailChangeNotificationSender
{
    public string Code { get; private set; } = "";
    public bool Fail { get; set; }
    public Task SendConfirmationAsync(string email, string code, CancellationToken ct)
    {
        if (Fail) throw new InvalidOperationException("Test transport unavailable");
        Code = code;
        return Task.CompletedTask;
    }
}

internal static class SensitiveIdentityTestSupport
{
    public static SensitiveIdentityService Service(ISensitiveIdentityDbContext db, EmailConfirmationRecorder? sender = null) =>
        new(db, new PasswordHasher(), new NoOpLoginAccountProtection(),
            new SessionLifetimePolicy(new AuthenticationSessionOptions(), TimeProvider.System),
            sender ?? new(), Options.Create(new SensitiveIdentityOptions()));

    public static CurrentUserContext Actor(AuthenticationSession s) =>
        new(s.UsuarioClinica.UserId, s.UsuarioClinica.User.PerfilId, "Test", s.UsuarioClinica.ClinicaId,
            UsuarioGlobalId: s.UsuarioGlobalId, UsuarioClinicaId: s.UsuarioClinicaId) { SessionId = s.Id };

    public static async Task<CurrentUserContext> SeedActorAsync(ISensitiveIdentityDbContext db, User user)
    {
        var global = new UsuarioGlobal { Email = user.Email, Nome = user.Nome, Senha = user.Senha };
        var session = new AuthenticationSession
        {
            Id = Guid.NewGuid(), UsuarioGlobal = global, SecurityVersion = global.SecurityVersion,
            UsuarioClinica = new UsuarioClinica { User = user, UsuarioGlobal = global, ClinicaId = user.ClinicaId,
                PerfilId = user.PerfilId, Clinica = db is HemodinksAPI.Infrastructure.Data.AppDbContext app
                    ? await app.Clinicas.FindAsync(user.ClinicaId) ?? new Clinica { Id = user.ClinicaId, Nome = "Test" }
                    : new Clinica { Id = user.ClinicaId, Nome = "Test" } }, RefreshTokenHash = Guid.NewGuid().ToString()
        };
        db.AuthenticationSessions.Add(session);
        await db.SaveChangesAsync();
        return Actor(session);
    }
}
