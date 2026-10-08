using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task SensitiveIdentity_EmailChangesOnlyAfterConfirmation_ThenOldSessionIsDenied()
    {
        var sender = new EmailConfirmationRecorder();
        var logs = new PasswordPolicyLogCapture();
        using var factory = new HemodinksApiFactory(s => {
            s.AddSingleton<IEmailChangeNotificationSender>(sender);
            s.AddSingleton<ILoggerProvider>(logs);
        });
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var response = await client.PostAsJsonAsync("/api/users/email/change", new { senhaAtual = TestPasswords.Valid, novoEmail = "verified@example.com" });
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var pending = (await response.Content.ReadFromJsonAsync<EmailChangeStarted>())!;
        string proofHash;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            proofHash = (await db.EmailChangeRequests.SingleAsync()).CodeHash;
            Assert.True(await db.UsuariosGlobais.AnyAsync(x => x.Email == "gmarcone@gmail.com"));
            Assert.False(await db.UsuariosGlobais.AnyAsync(x => x.Email == "verified@example.com"));
        }
        var confirm = await client.PostAsJsonAsync("/api/users/email/change/confirm", new { requestId = pending.RequestId, code = sender.Code });
        confirm.EnsureSuccessStatusCode();
        Assert.False(confirm.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session/clinicas")).StatusCode);
        var logged = string.Join("\n", logs.Messages);
        Assert.DoesNotContain(sender.Code, logged);
        Assert.DoesNotContain(proofHash, logged);
        Assert.DoesNotContain(client.DefaultRequestHeaders.Authorization!.Parameter!, logged);
        Assert.DoesNotContain(TestPasswords.Valid, logged);
        Assert.DoesNotContain("verified@example.com", logged);
    }

    [Fact]
    public async Task SensitiveIdentity_ClinicRoundTripInvalidatesPendingEmailProof()
    {
        var sender = new EmailConfirmationRecorder();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<IEmailChangeNotificationSender>(sender));
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var requested = await client.PostAsJsonAsync("/api/users/email/change", new { senhaAtual = TestPasswords.Valid, novoEmail = "pending@example.com" });
        requested.EnsureSuccessStatusCode();
        var pending = (await requested.Content.ReadFromJsonAsync<EmailChangeStarted>())!;
        int otherClinic;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var clinic = new Clinica { Nome = "Identity context", Slug = "identity-context" };
            db.Clinicas.Add(clinic);
            await db.SaveChangesAsync();
            otherClinic = clinic.Id;
        }
        foreach (var clinic in new[] { otherClinic, Clinica.DefaultId })
        {
            var switched = await client.PostAsJsonAsync("/api/session/selecionar-clinica", new { clinicaId = clinic });
            switched.EnsureSuccessStatusCode();
            var result = (await switched.Content.ReadFromJsonAsync<SelectClinicResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        }
        var confirm = await client.PostAsJsonAsync("/api/users/email/change/confirm", new { requestId = pending.RequestId, code = sender.Code });
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
        (await client.GetAsync("/api/session/clinicas")).EnsureSuccessStatusCode();
        using var verifyScope = factory.Services.CreateScope();
        Assert.False(await verifyScope.ServiceProvider.GetRequiredService<PlatformDbContext>().UsuariosGlobais.AnyAsync(x => x.Email == "pending@example.com"));
    }

    [Fact]
    public async Task SensitiveIdentity_WrongCurrentPassword_DoesNotLogOutOrDeleteCookie()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var denied = await client.PostAsJsonAsync("/api/users/email/change", new { senhaAtual = "incorrect-password", novoEmail = "verified@example.com" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("identity_revalidation_failed", await denied.Content.ReadAsStringAsync());
        Assert.False(denied.Headers.Contains("Set-Cookie"));
        (await client.GetAsync("/api/session/clinicas")).EnsureSuccessStatusCode();
        (await client.GetAsync("/api/dashboard/summary")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SensitiveIdentity_OrdinaryProfileUpdateCannotChangeAuthenticationEmail()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        int id;
        using (var scope = factory.Services.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Users.SingleAsync(x => x.Email == "gmarcone@gmail.com")).Id;
        var update = await client.PutAsJsonAsync($"/api/users/{id}", new { nome = "Super Admin", email = "unconfirmed@example.com", perfilId = 6, ativo = true });
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Contains("email_confirmation_required", await update.Content.ReadAsStringAsync());
        (await client.GetAsync("/api/session/clinicas")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SensitiveIdentity_PasswordChangeUsesCurrentPasswordOnce_ThenRevokesSession()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        int id;
        using (var scope = factory.Services.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Users.SingleAsync(x => x.Email == "gmarcone@gmail.com")).Id;
        var response = await client.PutAsJsonAsync($"/api/users/{id}/password", new { senhaAtual = TestPasswords.Valid, novaSenha = "a new individual passphrase 2026" });
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session/clinicas")).StatusCode);
    }
}
