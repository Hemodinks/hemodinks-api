using HemodinksAPI.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HemodinksAPI.Tests;

public class LocalUserSecretsConfigurationExtensionsTests
{
    [Fact]
    public void BuildFallbackValues_WhenEnvironmentIsNonProduction_UsesLocalSecretsForMissingRequiredKeys()
    {
        var currentConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Issuer"] = "HemodinksAPI"
            })
            .Build();

        var userSecretsConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost,14330;Database=HemodinksDBLocal;",
                ["JwtSettings:SecretKey"] = "segredo_local_com_32_caracteres_123"
            })
            .Build();

        var fallbackValues = LocalUserSecretsConfigurationExtensions.BuildFallbackValues(
            currentConfiguration,
            userSecretsConfiguration,
            new TestHostEnvironment(Environments.Staging));

        Assert.Equal("Server=localhost,14330;Database=HemodinksDBLocal;", fallbackValues["ConnectionStrings:DefaultConnection"]);
        Assert.Equal("segredo_local_com_32_caracteres_123", fallbackValues["JwtSettings:SecretKey"]);
    }

    [Fact]
    public void BuildFallbackValues_WhenConfigurationAlreadyHasValues_DoesNotOverrideThem()
    {
        var currentConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=sqlserver;Database=ConfiguredDb;",
                ["JwtSettings:SecretKey"] = "segredo_configurado_com_32_caracteres"
            })
            .Build();

        var userSecretsConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost,14330;Database=HemodinksDBLocal;",
                ["JwtSettings:SecretKey"] = "segredo_local_com_32_caracteres_123"
            })
            .Build();

        var fallbackValues = LocalUserSecretsConfigurationExtensions.BuildFallbackValues(
            currentConfiguration,
            userSecretsConfiguration,
            new TestHostEnvironment("Confirmation"));

        Assert.Empty(fallbackValues);
    }

    [Fact]
    public void BuildFallbackValues_WhenEnvironmentIsProduction_ReturnsEmptyFallback()
    {
        var currentConfiguration = new ConfigurationBuilder().Build();
        var userSecretsConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost,14330;Database=HemodinksDBLocal;",
                ["JwtSettings:SecretKey"] = "segredo_local_com_32_caracteres_123"
            })
            .Build();

        var fallbackValues = LocalUserSecretsConfigurationExtensions.BuildFallbackValues(
            currentConfiguration,
            userSecretsConfiguration,
            new TestHostEnvironment(Environments.Production));

        Assert.Empty(fallbackValues);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "HemodinksAPI.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
