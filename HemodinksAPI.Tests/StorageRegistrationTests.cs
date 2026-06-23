using HemodinksAPI.Api;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HemodinksAPI.Tests;

public class StorageRegistrationTests
{
    [Fact]
    public void AddStorage_WhenAzureConnectionStringIsMissingInDevelopment_RegistersLocalDiskStorage()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder().Build();

        services.AddStorage(configuration, new TestWebHostEnvironment(Environments.Development));

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<LocalDiskProfilePhotoStorage>(serviceProvider.GetRequiredService<IProfilePhotoStorage>());
        Assert.IsType<LocalDiskPatientFileStorage>(serviceProvider.GetRequiredService<IPatientFileStorage>());
    }

    [Fact]
    public void AddStorage_WhenAzureConnectionStringIsConfigured_RegistersAzureStorage()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true"
            })
            .Build();

        services.AddStorage(configuration, new TestWebHostEnvironment(Environments.Development));

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<AzureBlobProfilePhotoStorage>(serviceProvider.GetRequiredService<IProfilePhotoStorage>());
        Assert.IsType<AzureBlobPatientFileStorage>(serviceProvider.GetRequiredService<IPatientFileStorage>());
    }

    [Fact]
    public void AddStorage_WhenAzureConnectionStringIsMissingInProduction_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddStorage(configuration, new TestWebHostEnvironment(Environments.Production)));

        Assert.Equal("AzureStorage:ConnectionString must be configured in production.", exception.Message);
    }

    private sealed class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "HemodinksAPI.Tests";

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
