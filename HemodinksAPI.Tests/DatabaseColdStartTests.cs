using HemodinksAPI.Api;
using HemodinksAPI.Application.Data;
using Microsoft.Data.Sqlite;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace HemodinksAPI.Tests;

public sealed class DatabaseColdStartTests
{
    [Fact]
    public async Task Deployment_managed_startup_does_not_resolve_database_services()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:SchemaManagedByDeployment"] = "true"
        });
        await using var app = builder.Build();

        // No DbContext is registered: even constructing one would fail this test.
        await DatabaseStartupInitializer.InitializeAsync(app);
    }

    [Theory]
    [InlineData("Database:RunMigrationsOnStartup")]
    [InlineData("Database:RunMaintenanceOnStartup")]
    [InlineData("Seed:CbhpmOnStartup")]
    [InlineData("Seed:UsersOnStartup")]
    public async Task Explicit_startup_work_is_not_skipped(string setting)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:SchemaManagedByDeployment"] = "true",
            [setting] = "true"
        });
        await using var app = builder.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseStartupInitializer.InitializeAsync(app));
    }

    [Theory]
    [InlineData(false, HealthStatus.Unhealthy)]
    [InlineData(true, HealthStatus.Healthy)]
    public async Task Only_deployment_managed_readiness_skips_pending_schema(bool managed, HealthStatus expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Database:SchemaManagedByDeployment"] = managed.ToString() }).Build());
        services.AddScoped(_ => managed ? throw new InvalidOperationException("EF must not be resolved") : new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options,
            ClinicaContextFactory.CreatePlatform()));
        services.AddSingleton<ISqlConnectionFactory>(new TestingSqlConnectionFactory(() => new SqliteConnection("Data Source=:memory:")));
        services.AddScoped<IDatabaseReadinessProbe, SqlDatabaseReadinessProbe>();
        services.AddScoped<Func<AppDbContext>>(provider => () => provider.GetRequiredService<AppDbContext>());
        await using var provider = services.BuildServiceProvider();
        var check = new DatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expected, result.Status);
        if (!managed)
        {
            Assert.Equal("Banco com migrations pendentes", result.Description);
        }
    }

    [Fact]
    public async Task Deployment_managed_readiness_still_rejects_unavailable_database()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Database:SchemaManagedByDeployment"] = "true" }).Build());
        var missingDatabase = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.db");
        services.AddSingleton<ISqlConnectionFactory>(new TestingSqlConnectionFactory(
            () => new SqliteConnection($"Data Source={missingDatabase};Mode=ReadOnly")));
        services.AddScoped<IDatabaseReadinessProbe, SqlDatabaseReadinessProbe>();
        services.AddScoped<Func<AppDbContext>>(_ => () => throw new InvalidOperationException("EF must not be resolved"));
        await using var provider = services.BuildServiceProvider();
        var check = new DatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Banco indisponivel", result.Description);
    }
}
