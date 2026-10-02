using HemodinksAPI.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace HemodinksAPI.Tests;

public sealed class SessionLifetimeConfigurationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("8761")]
    [InlineData("2147483647")]
    [InlineData("not-a-number")]
    public void StartupRejectsInvalidAbsoluteLifetime(string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = new string('s', 64), ["JwtSettings:Issuer"] = "test", ["JwtSettings:Audience"] = "test",
            ["AuthenticationSession:AbsoluteLifetimeHours"] = value
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAuth(configuration, new Environment()));
    }

    private sealed class Environment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "HemodinksAPI";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
