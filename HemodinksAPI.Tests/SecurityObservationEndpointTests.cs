using System.Collections.Concurrent;
using Serilog;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed class SecurityObservationEndpointTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginAndSessionRemainCorrectWhenObservationThrows(bool unavailable)
    {
        var sink = new CaptureWriter { Unavailable = unavailable };
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<ISecurityObservationWriter>(sink));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Clinica-Slug", Clinica.DefaultSlug);
        var rejected = await client.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = "invalid-secret" });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
        var response = await client.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid });
        response.EnsureSuccessStatusCode();
        var identity = (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        var active = await client.GetAsync("/api/session/clinicas");
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.False(active.Headers.Contains("Set-Cookie"));
        using var logoutClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        logoutClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        logoutClient.DefaultRequestHeaders.Add("Origin", "https://hemodinks.gestao-saude.tec.br");
        logoutClient.DefaultRequestHeaders.Add("X-Session-Refresh", "1");
        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(identity.Token).Claims.ToDictionary(x => x.Type, x => x.Value);
        var logout = await logoutClient.PostAsJsonAsync("/api/session/sair", new { sessionId = claims["sid"], membershipId = int.Parse(claims["usuarioClinicaId"]) });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.False(logout.Headers.Contains("Set-Cookie")); // A bearer may revoke itself without clearing a different cookie.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session/clinicas")).StatusCode);
        if (!unavailable)
        {
            Assert.Single(sink.Events, x => x.Kind == SecurityEventKind.SessionRevoked);
            var failure = Assert.Single(sink.Events, x => x.Kind == SecurityEventKind.AuthenticationRefused);
            Assert.Null(failure.ClinicId);
            Assert.Equal(64, failure.AccountKey!.Length);
            var success = Assert.Single(sink.Events, x => x.Kind == SecurityEventKind.AuthenticationSucceeded);
            Assert.Equal(identity.ClinicaId, success.ClinicId);
            Assert.DoesNotContain(sink.Events, x => x.RequestId.Contains("invalid-secret"));
        }
    }

    [Fact]
    public async Task SecurityMonitoringEnforcesRolesAndTenantScopeFromServerClaims()
    {
        var reader = new CaptureReader();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<ISecurityObservationReader>(reader));
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/monitoramento/seguranca")).StatusCode);
        var loginResponse = await client.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid });
        loginResponse.EnsureSuccessStatusCode();
        var identity = (await loginResponse.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/monitoramento/seguranca?clinicaId=999")).StatusCode);
        Assert.Null(reader.Scope); // Explicit SuperAdministrador policy claim.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == identity.Id);
        user.PerfilId = Perfil.AdministradorId;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/monitoramento/seguranca?clinicaId=999")).StatusCode);
        Assert.Equal(identity.ClinicaId, reader.Scope); // Current DB role wins over old JWT superadmin claim.
        user.PerfilId = Perfil.MedicosId;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/monitoramento/seguranca")).StatusCode);
    }

    [Fact]
    public void ReaderDoesNotExposeOtherClinicsOrPlatformPreloginEvents()
    {
        var path = Path.Combine(Path.GetTempPath(), "security-monitoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "logs"));
        try
        {
            using (var logger = new Serilog.LoggerConfiguration().WriteTo.File(new Serilog.Formatting.Json.JsonFormatter(),
                Path.Combine(path, "logs", "hemodinks-security-test.json")).CreateLogger())
            {
                foreach (int? clinic in new int?[] { 1, 2, null })
                    logger.Information("Security {SecurityEvent} {Operation} {Reason} {RequestId} {ClinicId} {ObservedAt}",
                        "AuthenticationRefused", "login", "invalid_credential", "safe-id", clinic, DateTimeOffset.UtcNow);
            }
            var reader = new SecurityObservationReader(new EnvironmentStub { ContentRootPath = path });
            Assert.Equal(1, Assert.Single(reader.Read(1, 25, 1).Items).ClinicId);
            Assert.Equal(2, Assert.Single(reader.Read(1, 25, 2).Items).ClinicId);
            Assert.Equal(3, reader.Read(1, 25, null).Items.Count);
            Assert.Empty(reader.Read(1, 25, 999).Items);
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public async Task DiscoveryThrottlingAndRecoveryEmitMinimizedPlatformEvents()
    {
        var sink = new CaptureWriter();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<ISecurityObservationWriter>(sink));
        using var client = factory.CreateClient();
        var recovery = await client.PostAsJsonAsync("/api/users/password/reset", new { email = "unknown-security-test@example.invalid" });
        recovery.EnsureSuccessStatusCode();
        Assert.Null(Assert.Single(sink.Events, x => x.Kind == SecurityEventKind.RecoveryRequested).ClinicId);
        for (var i = 0; i < 10; i++)
        {
            var refused = await client.PostAsJsonAsync("/api/users/login-context", new { email = "unknown-security-test@example.invalid", senha = "private-secret" });
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }
        var limited = await client.PostAsJsonAsync("/api/users/login-context", new { email = "other@example.invalid", senha = "another-secret" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Null(Assert.Single(sink.Events, x => x.Kind == SecurityEventKind.Throttled).ClinicId);
        Assert.All(sink.Events, value => Assert.Null(value.ClinicId));
        var json = System.Text.Json.JsonSerializer.Serialize(sink.Events);
        Assert.DoesNotContain("private-secret", json);
        Assert.DoesNotContain("example.invalid", json);
    }

    [Fact]
    public async Task RefreshAbsoluteExpiryIsObservedWithoutRemovingCookie()
    {
        var sink = new CaptureWriter(); var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => { s.AddSingleton<ISecurityObservationWriter>(sink); s.AddSingleton<TimeProvider>(clock); });
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid });
        response.EnsureSuccessStatusCode();
        var identity = (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(identity.Token).Claims.ToDictionary(x => x.Type, x => x.Value);
        clock.Now = clock.Now.AddHours(12);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var session = await db.AuthenticationSessions.SingleAsync();
            session.LastActivityAt = clock.Now.UtcDateTime;
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("Origin", "https://hemodinks.gestao-saude.tec.br");
        client.DefaultRequestHeaders.Add("X-Session-Refresh", "1");
        var refresh = await client.PostAsJsonAsync("/api/session/renovar", new { sessionId = claims["sid"], membershipId = int.Parse(claims["usuarioClinicaId"]) });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.False(refresh.Headers.Contains("Set-Cookie"));
        Assert.Equal("absolute_expired", Assert.Single(sink.Events, x => x.Kind == SecurityEventKind.SessionExpired).Reason);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task MeasureLocalLoginWithAndWithoutEmission()
    {
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<ISecurityObservationWriter>(sp =>
            new SwitchingWriter(sp.GetRequiredService<SecurityObservationWorker>())));
        using var client = factory.CreateClient();
        var writer = (SwitchingWriter)factory.Services.GetRequiredService<ISecurityObservationWriter>();
        async Task<double> Login(bool enabled)
        {
            writer.Enabled = enabled;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var response = await client.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid });
            response.EnsureSuccessStatusCode();
            return watch.Elapsed.TotalMilliseconds;
        }
        await Login(true); // Startup/JIT excluded. Alternate samples to reduce warmup bias.
        var baseline = new List<double>(); var observed = new List<double>();
        for (var i = 0; i < 4; i++) { baseline.Add(await Login(false)); observed.Add(await Login(true)); }
        output.WriteLine($"Local login HTTP, warm process/in-memory DB, 4 alternating samples each: emission off {baseline.Average():F2} ms; on {observed.Average():F2} ms. Small sample, not production latency or statistical overhead estimate.");
    }

    private sealed class SwitchingWriter(SecurityObservationWorker worker) : ISecurityObservationWriter
    {
        public bool Enabled;
        public string? PseudonymizeAccount(string? email) => Enabled ? worker.PseudonymizeAccount(email) : null;
        public bool TryWrite(SecurityObservation value) => !Enabled || worker.TryWrite(value);
    }

    private sealed class CaptureWriter : ISecurityObservationWriter
    {
        public bool Unavailable;
        public ConcurrentQueue<SecurityObservation> Events = new();
        private readonly SecurityObservationWorker _pseudonyms = new(Microsoft.Extensions.Options.Options.Create(new SecurityObservationOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityObservationWorker>.Instance, TimeProvider.System);
        public string? PseudonymizeAccount(string? email) => Unavailable ? throw new IOException() : _pseudonyms.PseudonymizeAccount(email);
        public bool TryWrite(SecurityObservation value)
        {
            if (Unavailable) throw new IOException();
            Events.Enqueue(value); return true;
        }
    }
    private sealed class CaptureReader : ISecurityObservationReader
    {
        public int? Scope;
        public SecurityObservationPage Read(int page, int pageSize, int? clinicId)
        { Scope = clinicId; return new([], page, pageSize); }
    }
    private sealed class EnvironmentStub : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
