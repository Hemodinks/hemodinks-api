using HemodinksAPI.Api;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HemodinksAPI.Tests;

public sealed class DatabaseStartupPolicyTests
{
    [Fact]
    public void Migration_context_discovers_latest_schema_change()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true")
            .Options;

        using var context = new AppDbContext(options, ClinicaContextFactory.CreatePlatform());

        Assert.Contains("20260831150919_AddLoginAccountProtection", context.Database.GetMigrations());
    }

    [Fact]
    public void Production_RunsMigrationsOnStartup_WhenExplicitlyEnabled()
    {
        var environment = new TestHostEnvironment(Environments.Production);
        var configuration = BuildConfiguration(("Database:RunMigrationsOnStartup", "true"));

        Assert.True(DatabaseStartupInitializer.ShouldRunMigrations(environment, configuration));
    }

    [Fact]
    public void Development_UsesSafeDevelopmentDefault()
    {
        var environment = new TestHostEnvironment(Environments.Development);
        var configuration = BuildConfiguration();

        Assert.True(DatabaseStartupInitializer.ShouldRunMigrations(environment, configuration));
    }

    [Fact]
    public void NonProduction_UsesEnabledDefault()
    {
        var environment = new TestHostEnvironment(Environments.Staging);
        var configuration = BuildConfiguration();

        Assert.True(DatabaseStartupInitializer.ShouldRunMigrations(environment, configuration));
    }

    [Fact]
    public void Production_SkipsStartupMaintenance_WhenExplicitlyDisabled()
    {
        var environment = new TestHostEnvironment(Environments.Production);
        var configuration = BuildConfiguration(("Database:RunMaintenanceOnStartup", "false"));

        Assert.False(DatabaseStartupInitializer.ShouldRunMaintenance(environment, configuration));
    }

    [Fact]
    public void Development_RunsStartupMaintenance_ByDefault()
    {
        var environment = new TestHostEnvironment(Environments.Development);
        var configuration = BuildConfiguration();

        Assert.True(DatabaseStartupInitializer.ShouldRunMaintenance(environment, configuration));
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HemodinksAPI.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
