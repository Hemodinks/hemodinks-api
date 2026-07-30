using HemodinksAPI.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HemodinksAPI.Tests;

public sealed class DatabaseStartupPolicyTests
{
    [Fact]
    public void Production_NeverRunsMigrationsOnStartup_EvenWhenEnabled()
    {
        var environment = new TestHostEnvironment(Environments.Production);
        var configuration = BuildConfiguration(("Database:RunMigrationsOnStartup", "true"));

        Assert.False(DatabaseStartupInitializer.ShouldRunMigrations(environment, configuration));
    }

    [Fact]
    public void Development_UsesSafeDevelopmentDefault()
    {
        var environment = new TestHostEnvironment(Environments.Development);
        var configuration = BuildConfiguration();

        Assert.True(DatabaseStartupInitializer.ShouldRunMigrations(environment, configuration));
    }

    [Fact]
    public void NonDevelopment_UsesSafeDisabledDefault()
    {
        var environment = new TestHostEnvironment(Environments.Staging);
        var configuration = BuildConfiguration();

        Assert.False(DatabaseStartupInitializer.ShouldRunMigrations(environment, configuration));
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
