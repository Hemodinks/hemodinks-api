using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed partial class PasswordResetSecurityEndpointTests
{
    [Fact]
    public async Task TokenRecovery_RevokesSameIdentityAcrossClinics_WithoutChangingOtherUsersOrMemberships()
    {
        var sender = new RecordingPasswordResetNotificationSender();
        using var factory = CreateFactory(sender);
        using var firstClient = CreateClient(factory);
        var target = await Login(firstClient, Email, TestPasswords.Valid);
        firstClient.DefaultRequestHeaders.Add("X-Clinica-Id", target.User.ClinicaId.ToString());
        int secondClinicId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var clinic = new Clinica { Nome = "Recovery isolation", Slug = "recovery-isolation" };
            db.Clinicas.Add(clinic);
            await db.SaveChangesAsync();
            secondClinicId = clinic.Id;
            var original = await db.Users.SingleAsync(x => x.Id == target.User.Id);
            foreach (var (email, clinicId) in new[]
            {
                (Email, secondClinicId), ("unrelated-local@example.com", target.User.ClinicaId),
                ("unrelated-remote@example.com", secondClinicId)
            })
            {
                var user = new User
                {
                    Nome = email, Email = email, Telefone = "11999999999", Senha = original.Senha,
                    ClinicaId = clinicId, PerfilId = Perfil.AdministradorId, PrecisaTrocarSenha = false
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                await GlobalIdentityService.EnsureForUserAsync(db, user, default);
            }
        }
        using var secondClient = CreateClient(factory);
        secondClient.DefaultRequestHeaders.Add("X-Clinica-Id", secondClinicId.ToString());
        var targetOtherClinic = await Login(secondClient, Email, TestPasswords.Valid);
        var unrelatedLocal = await Login(firstClient, "unrelated-local@example.com", TestPasswords.Valid);
        var unrelatedRemote = await Login(secondClient, "unrelated-remote@example.com", TestPasswords.Valid);
        Assert.Equal(target.User.UsuarioGlobalId, targetOtherClinic.User.UsuarioGlobalId);
        Assert.Equal(HttpStatusCode.OK, (await GetUsers(firstClient, target.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetUsers(secondClient, targetOtherClinic.Token)).StatusCode);

        (await firstClient.PostAsJsonAsync("/api/users/password/reset", new { email = Email })).EnsureSuccessStatusCode();
        var token = Assert.Single(sender.Notifications).Token;
        (await secondClient.PostAsJsonAsync("/api/users/password/reset", new { email = Email })).EnsureSuccessStatusCode();
        var otherClinicToken = sender.Notifications.Last().Token;
        (await secondClient.PostAsJsonAsync("/api/users/password/reset", new { email = "unrelated-remote@example.com" })).EnsureSuccessStatusCode();
        MembershipSnapshot[] memberships;
        string unrelatedPassword;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            memberships = await Memberships(db);
            unrelatedPassword = (await db.UsuariosGlobais.SingleAsync(x => x.Id == unrelatedRemote.User.UsuarioGlobalId)).Senha;
        }

        // Deliberately send the other clinic header: the validated token identifies the recovery target.
        (await secondClient.PostAsJsonAsync("/api/users/password/reset/confirm", new { token, novaSenha = NewPassword })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            Assert.Equal(memberships, await Memberships(db));
            var sessions = await db.AuthenticationSessions.ToListAsync();
            Assert.All(sessions.Where(x => x.UsuarioGlobalId == target.User.UsuarioGlobalId), x => Assert.NotNull(x.RevokedAt));
            Assert.All(sessions.Where(x => x.UsuarioGlobalId != target.User.UsuarioGlobalId), x => Assert.Null(x.RevokedAt));
            var other = await db.UsuariosGlobais.SingleAsync(x => x.Id == unrelatedRemote.User.UsuarioGlobalId);
            Assert.Equal(unrelatedRemote.Version, other.SecurityVersion);
            Assert.Equal(unrelatedPassword, other.Senha);
            Assert.Null((await db.PasswordResetTokens.SingleAsync(x => x.UserId == unrelatedRemote.User.Id)).UsedAt);
        }

        foreach (var previous in new[] { target, targetOtherClinic })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await GetUsers(firstClient, previous.Token)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(firstClient, previous)).StatusCode);
        }
        foreach (var other in new[] { unrelatedLocal, unrelatedRemote })
        {
            Assert.Equal(HttpStatusCode.OK, (await GetUsers(firstClient, other.Token)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Refresh(firstClient, other)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await secondClient.PostAsJsonAsync("/api/users/password/reset/confirm",
            new { token = otherClinicToken, novaSenha = "UnexpectedPassword@789" })).StatusCode);
        var newFirst = await Login(firstClient, Email, NewPassword);
        var newSecond = await Login(secondClient, Email, NewPassword);
        Assert.Equal(newFirst.Version, newSecond.Version);
        Assert.Equal(target.User.PerfilId, newFirst.User.PerfilId);
        Assert.Equal(targetOtherClinic.User.PerfilId, newSecond.User.PerfilId);
        Assert.Equal(target.User.ClinicaId, newFirst.User.ClinicaId);
        Assert.Equal(secondClinicId, newSecond.User.ClinicaId);

        var visible = await GetUsers(firstClient, newFirst.Token);
        visible.EnsureSuccessStatusCode();
        using var json = System.Text.Json.JsonDocument.Parse(await visible.Content.ReadAsStringAsync());
        Assert.Contains("unrelated-local@example.com", json.RootElement.GetRawText());
        Assert.DoesNotContain(json.RootElement.GetRawText(), "unrelated-remote@example.com");
    }

    private sealed record MembershipSnapshot(int Id, int GlobalId, int UserId, int ClinicId, int ProfileId, bool Active, bool Default);

    private static Task<MembershipSnapshot[]> Memberships(PlatformDbContext db) => db.UsuariosClinicas.AsNoTracking()
        .OrderBy(x => x.Id).Select(x => new MembershipSnapshot(x.Id, x.UsuarioGlobalId, x.UserId, x.ClinicaId,
            x.PerfilId, x.Ativo, x.ClinicaPadrao)).ToArrayAsync();
}
