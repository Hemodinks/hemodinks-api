using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Security;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Theory]
    [InlineData("change", false)]
    [InlineData("change", true)]
    [InlineData("clinic-admin", false)]
    [InlineData("clinic-admin", true)]
    [InlineData("clinic-team", false)]
    [InlineData("update-admin", false)]
    [InlineData("update-team", false)]
    [InlineData("team", false)]
    [InlineData("team", true)]
    [InlineData("user", true)]
    public async Task PasswordPolicy_BlocksNewCredentialsWithoutPersistingOrLoggingCandidates(string flow, bool unavailable)
    {
        var lookup = new SwitchablePasswordLookup();
        var logs = new PasswordPolicyLogCapture();
        using var factory = new HemodinksApiFactory(services => {
            services.AddSingleton<ICompromisedPasswordLookup>(lookup);
            services.AddSingleton<ILoggerProvider>(logs);
        });
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        lookup.Unavailable = unavailable;
        lookup.Calls = 0;
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Email == "gmarcone@gmail.com");
        var beforeUsers = await db.Users.CountAsync();
        var beforeClinics = await db.Clinicas.CountAsync();
        var password = unavailable ? "  sete barcos cruzam o luar  " : "qwertyuiop";
        object team = new { nome = "Equipe", email = "policy-team@example.com", senha = password, modoIdentificacao = "Pin" };
        using var response = flow switch {
            "change" => await client.PutAsJsonAsync($"/api/users/{user.Id}/password", new { senhaAtual = TestPasswords.Valid, novaSenha = password }),
            "team" => await client.PostAsJsonAsync("/api/equipes/", team),
            "update-admin" => await client.PutAsJsonAsync($"/api/platform/clinicas/{Clinica.DefaultId}", new { administradorNovaSenha = password }),
            "update-team" => await client.PutAsJsonAsync($"/api/platform/clinicas/{Clinica.DefaultId}", new { novaEquipe = team }),
            "user" => await client.PostAsJsonAsync("/api/users/", new { nome = "Novo usuario", email = "policy-user@example.com", telefone = "+5511999991111", perfilId = Perfil.AdministradorId }),
            _ => await client.PostAsJsonAsync("/api/platform/clinicas", new {
                nome = "Clinica politica", slug = "policy-clinic", cnpj = ValidCnpj,
                administradorNome = "Administrador", administradorEmail = "policy-admin@example.com",
                administradorSenha = flow == "clinic-team" ? TestPasswords.Valid : password,
                equipeInicial = flow == "clinic-team" ? team : null
            })
        };
        Assert.Equal(unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(unavailable ? "password_policy_unavailable" : CompromisedPasswordException.Code,
            body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("message").GetString()));
        Assert.True(lookup.Calls > 0);
        Assert.Equal(beforeUsers, await db.Users.CountAsync());
        Assert.Equal(beforeClinics, await db.Clinicas.CountAsync());
        Assert.Equal(user.Senha, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id)).Senha);
        var messages = string.Join("\n", logs.Messages);
        Assert.DoesNotContain(password, messages);
        Assert.DoesNotContain(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))), messages);
        Assert.DoesNotContain(Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password))), messages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordPolicy_RecoveryRejectionPreservesTokenAndPassword(bool unavailable)
    {
        var lookup = new SwitchablePasswordLookup();
        var sender = new RecordingPasswordResetNotificationSender();
        using var factory = new HemodinksApiFactory(services => {
            services.AddSingleton<ICompromisedPasswordLookup>(lookup);
            services.AddSingleton<HemodinksAPI.Application.Services.IPasswordResetNotificationSender>(sender);
        });
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/users/password/reset", new { email = "gmarcone@gmail.com" })).EnsureSuccessStatusCode();
        var token = sender.Notifications.Single().Token;
        lookup.Unavailable = unavailable;
        var response = await client.PostAsJsonAsync("/api/users/password/reset/confirm", new { token, novaSenha = "password" });
        Assert.Equal(unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(unavailable ? "password_policy_unavailable" : CompromisedPasswordException.Code,
            body.RootElement.GetProperty("code").GetString());
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.PasswordResetTokens.SingleAsync()).UsedAt);
        Assert.True(new PasswordHasher().VerifyPassword(TestPasswords.Valid,
            (await db.Users.SingleAsync(x => x.Email == "gmarcone@gmail.com")).Senha));
        lookup.Unavailable = false;
        const string phrase = "  sete barcos cruzam o luar da serra  ";
        var success = await client.PostAsJsonAsync("/api/users/password/reset/confirm", new { token, novaSenha = phrase });
        success.EnsureSuccessStatusCode();
        await AuthenticateAsync(client, senha: phrase);
    }

    [Fact]
    public async Task PasswordPolicy_LoginAndDiscoveryNeverConsultDataset()
    {
        var lookup = new SwitchablePasswordLookup();
        using var factory = new HemodinksApiFactory(services => services.AddSingleton<ICompromisedPasswordLookup>(lookup));
        using var client = factory.CreateClient();
        await AuthenticateAsync(client); // finish startup seeding before simulating unavailable reference data
        lookup.Unavailable = true;
        lookup.Calls = 0;
        await AuthenticateAsync(client);
        (await client.PostAsJsonAsync("/api/users/login-context", new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid })).EnsureSuccessStatusCode();
        Assert.Equal(0, lookup.Calls);
    }

    [Theory]
    [InlineData("team")]
    [InlineData("clinic")]
    public async Task PasswordPolicy_ProvisioningPreservesExactPassphrase(string flow)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        const string phrase = "  café sobre sete montanhas azuis  ";
        var email = "exact-phrase@example.com";
        var response = flow == "team"
            ? await client.PostAsJsonAsync("/api/equipes/", new { nome = "Equipe", email, senha = phrase, modoIdentificacao = "Pin" })
            : await client.PostAsJsonAsync("/api/platform/clinicas", new { nome = "Clínica", slug = "exact-phrase", cnpj = ValidCnpj,
                administradorNome = "Admin", administradorEmail = email, administradorSenha = phrase });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.SingleAsync(x => x.Email == email);
        var hasher = new PasswordHasher();
        Assert.True(hasher.VerifyPassword(phrase, stored.Senha));
        Assert.False(hasher.VerifyPassword(phrase.Trim(), stored.Senha));
    }

    private sealed class SwitchablePasswordLookup : ICompromisedPasswordLookup
    {
        private readonly LocalCompromisedPasswordLookup actual = new();
        public bool Unavailable;
        public int Calls;
        public PasswordLookupResult Check(string candidate) {
            Interlocked.Increment(ref Calls);
            return Unavailable ? PasswordLookupResult.Unavailable : actual.Check(candidate);
        }
    }

    private sealed class PasswordPolicyLogCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception) + exception?.ToString());
        }
    }
}
