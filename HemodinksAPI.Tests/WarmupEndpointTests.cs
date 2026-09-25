using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace HemodinksAPI.Tests;

public sealed class WarmupEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anonymous_endpoint_needs_no_context_and_returns_no_data(bool sendCredentials)
    {
        var probe = new RecordingProbe();
        using var factory = CreateFactory(probe);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        if (sendCredentials)
        {
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: "HemodinksAPI", audience: "HemodinksAPI",
                claims: [new Claim("sid", Guid.NewGuid().ToString()), new Claim("temporary_password", "true")],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef")),
                    SecurityAlgorithms.HmacSha256)));
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            client.DefaultRequestHeaders.Add("Cookie", "refresh=irrelevant");
            client.DefaultRequestHeaders.Add("X-Clinica-Slug", "must-not-resolve");
        }
        for (var i = 0; i < 2; i++)
        {
            using var response = await client.GetAsync("/api/warmup");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            Assert.False(response.Headers.Contains("X-Clinica-Slug"));
        }
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task Rate_limit_rejects_sixth_request_even_with_spoofed_forwarded_headers()
    {
        var probe = new RecordingProbe();
        using var factory = CreateFactory(probe);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        for (var i = 0; i < 6; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/warmup");
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            using var response = await client.SendAsync(request);
            Assert.Equal(i < 5 ? HttpStatusCode.NoContent : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        Assert.Equal(5, probe.Calls);
    }

    [Fact]
    public async Task Rate_limit_has_independent_budgets_per_remote_IP()
    {
        var probe = new RecordingProbe();
        using var factory = CreateFactory(probe);
        for (var i = 0; i < 7; i++)
        {
            var response = await factory.Server.SendAsync(context =>
            {
                context.Request.Scheme = "https";
                context.Request.Method = "GET";
                context.Request.Path = "/api/warmup";
                context.Connection.RemoteIpAddress = IPAddress.Parse(i < 6 ? "203.0.113.1" : "203.0.113.2");
            });
            Assert.Equal(i == 5 ? 429 : 204, response.Response.StatusCode);
        }
        Assert.Equal(6, probe.Calls);
    }

    [Fact]
    public async Task Azure_single_hop_forwarding_uses_ingress_appended_IP_not_client_prefix()
    {
        var probe = new RecordingProbe();
        using var factory = CreateFactory(probe, forwarded: true);
        for (var i = 0; i < 6; i++)
        {
            var response = await factory.Server.SendAsync(context =>
            {
                context.Request.Scheme = "https";
                context.Request.Method = "GET";
                context.Request.Path = "/api/warmup";
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                context.Request.Headers["X-Forwarded-For"] = $"203.0.113.{i + 1}, 198.51.100.1";
            });
            Assert.Equal(i < 5 ? 204 : 429, response.Response.StatusCode);
        }
        Assert.Equal(5, probe.Calls);
    }

    [Fact]
    public async Task Public_branch_does_not_bypass_security_of_business_routes()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        foreach (var path in new[] { "/api/users", "/api/warmup/users" })
        {
            using var response = await client.GetAsync(path);
            Assert.False(response.IsSuccessStatusCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_database_returns_generic_empty_failure(bool throws)
    {
        using var factory = CreateFactory(new RecordingProbe
        {
            Operation = _ => throws
                ? throw new InvalidOperationException("secret connection details")
                : Task.FromResult(new DatabaseReadinessResult(false, []))
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/api/warmup");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disabled_warmup_does_not_touch_database()
    {
        var probe = new RecordingProbe();
        using var factory = CreateFactory(probe, enabled: false);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/warmup")).StatusCode);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task Cancellation_reaches_probe_and_is_handled_without_details()
    {
        var probe = new RecordingProbe
        {
            Operation = async token => { await Task.Delay(Timeout.Infinite, token); return new(true, []); }
        };
        using var cancellation = new CancellationTokenSource();
        var task = WarmupEndpointExtensions.HandleAsync(new DefaultHttpContext(), probe,
            new ConfigurationBuilder().Build(), NullLoggerFactory.Instance, cancellation.Token);
        cancellation.Cancel();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(503, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.True(probe.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Other_methods_do_not_execute_warmup()
    {
        var probe = new RecordingProbe();
        using var factory = CreateFactory(probe);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/api/warmup", null)).StatusCode);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task Existing_probe_works_repeatedly_on_empty_database_without_resolving_EF_or_writing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"warmup-{Guid.NewGuid():N}.db");
        try
        {
            var probe = new SqlDatabaseReadinessProbe(
                new TestingSqlConnectionFactory(() => new SqliteConnection($"Data Source={path};Pooling=False")),
                () => throw new InvalidOperationException("Warmup must not resolve schema/tenant context"));
            for (var i = 0; i < 2; i++)
                Assert.True((await probe.CheckAsync(false, CancellationToken.None)).Connected);
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.CheckAsync(false, cancelled.Token));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task Existing_probe_executes_on_SQL_Server_without_schema_or_business_data()
    {
        var probe = new SqlDatabaseReadinessProbe(
            new SqlConnectionFactory(SqlServerTestConnection.Create("master")),
            () => throw new InvalidOperationException("Warmup must not resolve EF"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Assert.True((await probe.CheckAsync(false, timeout.Token)).Connected);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory(
        RecordingProbe probe, bool enabled = true, bool forwarded = false) => new HemodinksApiFactory(services =>
        {
            services.AddSingleton<IDatabaseReadinessProbe>(probe);
            services.AddScoped<AppDbContext>(_ => throw new InvalidOperationException("Unexpected tenant context"));
            services.AddScoped<PlatformDbContext>(_ => throw new InvalidOperationException("Unexpected platform context"));
        }).WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Warmup:Enabled"] = enabled.ToString(),
                ["ForwardedHeaders:Enabled"] = forwarded.ToString(),
                ["ForwardedHeaders:TrustAnyImmediateProxy"] = forwarded.ToString(),
                ["ForwardedHeaders:ForwardLimit"] = "1",
                ["Database:RunMigrationsOnStartup"] = "false",
                ["Database:SchemaManagedByDeployment"] = "true",
                ["Database:RunMaintenanceOnStartup"] = "false",
                ["Seed:CbhpmOnStartup"] = "false",
                ["Seed:UsersOnStartup"] = "false"
            })));

    private sealed class RecordingProbe : IDatabaseReadinessProbe
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public Func<CancellationToken, Task<DatabaseReadinessResult>> Operation { get; init; } =
            _ => Task.FromResult(new DatabaseReadinessResult(true, []));

        public Task<DatabaseReadinessResult> CheckAsync(bool validateSchema, CancellationToken cancellationToken)
        {
            Assert.False(validateSchema);
            Assert.True(cancellationToken.CanBeCanceled);
            Calls++;
            Token = cancellationToken;
            return Operation(cancellationToken);
        }
    }
}
