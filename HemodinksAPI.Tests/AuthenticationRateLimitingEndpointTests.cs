using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HemodinksAPI.Tests;

public sealed class AuthenticationRateLimitingEndpointTests
{
    [Fact]
    public async Task AccountLimit_DoesNotBlockAnotherAccountOnSameIp_AndHasStableContract()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://hemodinks.gestao-saude.tec.br");
        client.DefaultRequestHeaders.Add("X-Session-Refresh", "1");
        for (var i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/users/login-context",
                new { email = "attacked@example.invalid", senha = "invalid" })).StatusCode);
        var limited = await client.PostAsJsonAsync("/api/users/login-context",
            new { email = " ATTACKED@example.invalid ", senha = "invalid" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.True(limited.Headers.CacheControl?.NoStore);
        Assert.Contains("Retry-After", limited.Headers.GetValues("Access-Control-Expose-Headers").Single());
        using var payload = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.Equal("rate_limited", payload.RootElement.GetProperty("code").GetString());
        Assert.Equal((int)limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds,
            payload.RootElement.GetProperty("retryAfterSeconds").GetInt32());
        Assert.DoesNotContain("attacked", payload.RootElement.GetRawText());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/users/login-context",
            new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/users/authenticate",
            new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid })).StatusCode);
    }

    [Fact]
    public async Task MultipleLegitimateIndividualAndTeamAccounts_CanLoginOnSameIp()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var accounts = new[]
        {
            (Email: "gmarcone@gmail.com", Password: TestPasswords.Valid, Slug: HemodinksAPI.Domain.Models.Clinica.DefaultSlug),
            (Email: fixture.Selection.Email, Password: TeamLoginFixture.Password, Slug: fixture.Selection.Slug),
            (Email: fixture.Pin.Email, Password: TeamLoginFixture.Password, Slug: fixture.Pin.Slug)
        };
        for (var i = 0; i < 3; i++)
            foreach (var account in accounts)
            {
                client.DefaultRequestHeaders.Remove("X-Clinica-Slug");
                client.DefaultRequestHeaders.Add("X-Clinica-Slug", account.Slug);
                var credential = new { email = account.Email, senha = account.Password };
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/users/login-context", credential)).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/users/authenticate", credential)).StatusCode);
            }
    }
    [Fact]
    public async Task DeniedAccount_DoesNotSpendSharedIpBudget_AndClinicOrIpCannotBypassAccountLimit()
    {
        using var factory = Limited(login: 1, ip: 3);
        Assert.Equal(401, await Send(factory, "attacked@example.invalid", "203.0.113.1"));
        for (var i = 0; i < 6; i++)
            Assert.Equal(429, await Send(factory, "ATTACKED@example.invalid", $"203.0.113.{i + 2}", slug: $"clinic-{i}"));
        Assert.Equal(401, await Send(factory, "another@example.invalid", "203.0.113.1"));
        Assert.Equal(401, await Send(factory, "third@example.invalid", "203.0.113.1"));
        Assert.Equal(429, await Send(factory, "fourth@example.invalid", "203.0.113.1"));
        Assert.Equal(401, await Send(factory, "fifth@example.invalid", "203.0.113.2"));
    }

    [Fact]
    public async Task IndividualAndDiscovery_ShareSubjectBudget()
    {
        using var factory = Limited(login: 1);
        Assert.Equal(401, await Send(factory, "unknown@example.invalid", "203.0.113.1"));
        Assert.Equal(429, await Send(factory, "unknown@example.invalid", "203.0.113.2", path: "/api/users/authenticate"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginBudget_RejectsSpoofedForwardedHeaders(bool trustedProxy)
    {
        using var factory = Limited(ip: 2, forwarding: true);
        var origin = trustedProxy ? "127.0.0.1" : "203.0.113.5";
        for (var i = 0; i < 3; i++)
            Assert.Equal(i < 2 ? 401 : 429, await Send(factory, $"unknown{i}@example.invalid", origin,
                forwarded: $"198.51.100.{i + 1}, 192.0.2.1"));
    }

    [Fact]
    public async Task AzureIngress_ClientPrefixesCannotEvadeOriginBudget()
    {
        using var factory = Limited(ip: 2, azureIngress: true);
        for (var i = 0; i < 3; i++)
            Assert.Equal(i < 2 ? 401 : 429, await Send(factory, $"unknown{i}@example.invalid", "10.0.0.10",
                forwarded: $"203.0.113.{i + 1}, 198.51.100.1", proto: "https"));
    }

    [Fact]
    public async Task AzureIngress_DifferentClientsBehindSameProxyHaveSeparateOriginBudgets()
    {
        using var factory = Limited(ip: 1, azureIngress: true);
        Assert.Equal(401, await Send(factory, "first@example.invalid", "10.0.0.10", forwarded: "198.51.100.1", proto: "https"));
        Assert.Equal(429, await Send(factory, "second@example.invalid", "10.0.0.10", forwarded: "198.51.100.1", proto: "https"));
        Assert.Equal(401, await Send(factory, "third@example.invalid", "10.0.0.10", forwarded: "198.51.100.2", proto: "https"));
    }

    [Fact]
    public async Task AzureIngress_AccountBudgetCannotBeEvadedByChangingOriginOrClinic()
    {
        using var factory = Limited(login: 1, azureIngress: true);
        Assert.Equal(401, await Send(factory, "unknown@example.invalid", "10.0.0.10", forwarded: "198.51.100.1", proto: "https"));
        Assert.Equal(429, await Send(factory, "unknown@example.invalid", "10.0.0.10", slug: "other-clinic", forwarded: "198.51.100.2", proto: "https"));
    }

    [Fact]
    public async Task RecoveryBudgets_AreSeparateFromLoginAndOtherAccounts()
    {
        using var factory = new HemodinksApiFactory(s => s.Configure<AuthenticationRateLimitOptions>(o => o.RecoveryPermitLimit = 1));
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/users/password/reset", new { email = "unknown@example.invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/users/password/reset", new { email = " UNKNOWN@example.invalid " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/users/password/reset", new { email = "other@example.invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/users/login-context", new { email = "unknown@example.invalid", senha = "invalid" })).StatusCode);
        var invalid = new { token = "invalid-token", novaSenha = TestPasswords.Valid };
        var first = await client.PostAsJsonAsync("/api/users/password/reset/confirm", invalid);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/users/password/reset/confirm", invalid)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/users/password/reset/confirm", new { token = "other-token", novaSenha = TestPasswords.Valid })).StatusCode);
    }

    [Fact]
    public async Task SubjectBudget_RecoversAfterWindow()
    {
        using var factory = new HemodinksApiFactory(s => s.Configure<AuthenticationRateLimitOptions>(o =>
        { o.LoginPermitLimit = 1; o.SubjectWindowSeconds = 1; }));
        Assert.Equal(401, await Send(factory, "unknown@example.invalid", "203.0.113.1"));
        Assert.Equal(429, await Send(factory, "unknown@example.invalid", "203.0.113.1"));
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        Assert.Equal(401, await Send(factory, "unknown@example.invalid", "203.0.113.1"));
    }

    private static WebApplicationFactory<Program> Limited(int login = 30, int ip = 300, bool forwarding = false, bool azureIngress = false) =>
        new HemodinksApiFactory(s => s.Configure<AuthenticationRateLimitOptions>(o => { o.LoginPermitLimit = login; o.IpPermitLimit = ip; }))
            .WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(azureIngress
                ? ForwardedHeadersConfigurationTests.AzureValues()
                : new Dictionary<string, string?>
                { ["ForwardedHeaders:Enabled"] = forwarding.ToString(), ["ForwardedHeaders:KnownProxies:0"] = "127.0.0.1" })));

    private sealed class BodyDetection : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }

    private static async Task<int> Send(WebApplicationFactory<Program> factory, string email, string origin,
        string? slug = null, string? forwarded = null, string path = "/api/users/login-context", string? proto = null)
    {
        // Start the host before obtaining TestServer.
        using var client = factory.CreateClient();
        var response = await factory.Server.SendAsync(context =>
        {
            context.Request.Scheme = "https"; context.Request.Method = "POST"; context.Request.Path = path;
            context.Connection.RemoteIpAddress = IPAddress.Parse(origin);
            context.Request.Host = new HostString("localhost");
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection());
            context.Request.ContentType = "application/json";
            var body = JsonSerializer.SerializeToUtf8Bytes(new { email, senha = "invalid" });
            context.Request.ContentLength = body.Length;
            context.Request.Body = new MemoryStream(body);
            if (slug != null) context.Request.Headers["X-Clinica-Slug"] = slug;
            if (forwarded != null) context.Request.Headers["X-Forwarded-For"] = forwarded;
            if (proto != null) context.Request.Headers["X-Forwarded-Proto"] = proto;
        });
        return response.Response.StatusCode;
    }
}
