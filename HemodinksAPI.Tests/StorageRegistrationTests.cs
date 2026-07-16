using HemodinksAPI.Api;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Infrastructure.Queues;
using HemodinksAPI.Infrastructure.Services;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Infrastructure.Storage;
using HemodinksAPI.Infrastructure.PasswordReset;
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
    public void AddStorage_WhenStorageFunctionsAreConfigured_RegistersFunctionBackedUploads()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["StorageFunctions:BaseUrl"] = "https://hemodinks-workers-confirmation.azurewebsites.net",
                ["StorageFunctions:FunctionKey"] = "secret"
            })
            .Build();

        services.AddStorage(configuration, new TestWebHostEnvironment(Environments.Development));

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<FunctionBackedProfilePhotoStorage>(serviceProvider.GetRequiredService<IProfilePhotoStorage>());
        Assert.IsType<FunctionBackedPatientFileStorage>(serviceProvider.GetRequiredService<IPatientFileStorage>());
    }

    [Fact]
    public void AddStorage_WhenStorageFunctionKeyIsMissing_UsesAzureStorageDirectly()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["StorageFunctions:BaseUrl"] = "https://hemodinks-workers-production.azurewebsites.net"
            })
            .Build();

        services.AddStorage(configuration, new TestWebHostEnvironment(Environments.Development));

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<AzureBlobProfilePhotoStorage>(serviceProvider.GetRequiredService<IProfilePhotoStorage>());
        Assert.IsType<AzureBlobPatientFileStorage>(serviceProvider.GetRequiredService<IPatientFileStorage>());
    }

    [Fact]
    public void AddStorage_WhenStorageFunctionBaseUrlIsInvalid_UsesAzureStorageDirectly()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["StorageFunctions:BaseUrl"] = "hemodinks-workers-production.azurewebsites.net",
                ["StorageFunctions:FunctionKey"] = "secret"
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

    [Fact]
    public void AddApplicationServices_WhenAsyncQueuesAreDisabled_UsesSmtpAndDisablesExportQueue()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AsyncQueues:Enabled"] = "false"
            })
            .Build();

        services.AddApplicationServices(configuration, new TestWebHostEnvironment(Environments.Development));

        using var serviceProvider = services.BuildServiceProvider();

        AssertPasswordResetSenderPipeline(
            serviceProvider,
            typeof(SmtpPasswordResetNotificationSender));
        Assert.IsType<DisabledFileExportQueue>(serviceProvider.GetRequiredService<IFileExportQueue>());
    }

    [Fact]
    public void AddApplicationServices_WhenPasswordResetFunctionsAreConfigured_UsesFunctionBackedSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AsyncQueues:Enabled"] = "true",
                ["AsyncQueues:FileExportEnabled"] = "true",
                ["AsyncQueues:PasswordResetEnabled"] = "true",
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["PasswordResetFunctions:BaseUrl"] = "https://hemodinks-workers-confirmation.azurewebsites.net",
                ["PasswordResetFunctions:FunctionKey"] = "secret"
            })
            .Build();

        services.AddApplicationServices(configuration, new TestWebHostEnvironment(Environments.Development));

        using var serviceProvider = services.BuildServiceProvider();

        AssertPasswordResetSenderPipeline(
            serviceProvider,
            typeof(FunctionBackedPasswordResetNotificationSender),
            typeof(AzureQueuePasswordResetNotificationSender),
            typeof(SmtpPasswordResetNotificationSender));
        Assert.IsType<AzureFileExportQueue>(serviceProvider.GetRequiredService<IFileExportQueue>());
    }

    [Fact]
    public void AddApplicationServices_WhenPasswordResetFunctionBaseUrlIsInvalid_UsesAzureQueueSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AsyncQueues:Enabled"] = "true",
                ["AsyncQueues:FileExportEnabled"] = "true",
                ["AsyncQueues:PasswordResetEnabled"] = "true",
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["PasswordResetFunctions:BaseUrl"] = "hemodinks-workers-confirmation.azurewebsites.net",
                ["PasswordResetFunctions:FunctionKey"] = "secret"
            })
            .Build();

        services.AddApplicationServices(configuration, new TestWebHostEnvironment(Environments.Development));

        using var serviceProvider = services.BuildServiceProvider();

        AssertPasswordResetSenderPipeline(
            serviceProvider,
            typeof(AzureQueuePasswordResetNotificationSender),
            typeof(SmtpPasswordResetNotificationSender));
        Assert.IsType<AzureFileExportQueue>(serviceProvider.GetRequiredService<IFileExportQueue>());
    }

    [Fact]
    public void AddApplicationServices_WhenPasswordResetFunctionKeyIsMissing_UsesAzureQueueSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AsyncQueues:Enabled"] = "true",
                ["AsyncQueues:FileExportEnabled"] = "true",
                ["AsyncQueues:PasswordResetEnabled"] = "true",
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["PasswordResetFunctions:BaseUrl"] = "https://hemodinks-workers-confirmation.azurewebsites.net"
            })
            .Build();

        services.AddApplicationServices(configuration, new TestWebHostEnvironment(Environments.Development));

        using var serviceProvider = services.BuildServiceProvider();

        AssertPasswordResetSenderPipeline(
            serviceProvider,
            typeof(AzureQueuePasswordResetNotificationSender),
            typeof(SmtpPasswordResetNotificationSender));
        Assert.IsType<AzureFileExportQueue>(serviceProvider.GetRequiredService<IFileExportQueue>());
    }

    [Fact]
    public void AddApplicationServices_WhenAsyncQueuesAreEnabled_UsesAzureQueues()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AsyncQueues:Enabled"] = "true",
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true"
            })
            .Build();

        services.AddApplicationServices(configuration, new TestWebHostEnvironment(Environments.Development));

        using var serviceProvider = services.BuildServiceProvider();

        AssertPasswordResetSenderPipeline(
            serviceProvider,
            typeof(AzureQueuePasswordResetNotificationSender),
            typeof(SmtpPasswordResetNotificationSender));
        Assert.IsType<AzureFileExportQueue>(serviceProvider.GetRequiredService<IFileExportQueue>());
    }

    [Fact]
    public void AddApplicationServices_WhenOnlyFileExportQueueIsEnabled_UsesSmtpForResetAndAzureQueueForExports()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AsyncQueues:Enabled"] = "false",
                ["AsyncQueues:PasswordResetEnabled"] = "false",
                ["AsyncQueues:FileExportEnabled"] = "true",
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true"
            })
            .Build();

        services.AddApplicationServices(configuration, new TestWebHostEnvironment(Environments.Development));

        using var serviceProvider = services.BuildServiceProvider();

        AssertPasswordResetSenderPipeline(
            serviceProvider,
            typeof(SmtpPasswordResetNotificationSender));
        Assert.IsType<AzureFileExportQueue>(serviceProvider.GetRequiredService<IFileExportQueue>());
    }

    private static void AssertPasswordResetSenderPipeline(
        IServiceProvider serviceProvider,
        params Type[] expectedTransportTypes)
    {
        Assert.IsType<FallbackPasswordResetNotificationSender>(
            serviceProvider.GetRequiredService<IPasswordResetNotificationSender>());

        var transportTypes = serviceProvider
            .GetServices<IPasswordResetNotificationTransport>()
            .Select(transport => transport.GetType())
            .ToArray();

        Assert.Equal(expectedTransportTypes, transportTypes);
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
