using System.Net;
using HemodinksAPI.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public class ForwardedHeadersConfigurationTests
{
    [Fact]
    public void AddProxyForwarding_WhenDisabled_DoesNotProcessForwardedHeaders()
    {
        var options = ResolveOptions(new Dictionary<string, string?>());

        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.NotEmpty(options.KnownProxies);
        Assert.NotEmpty(options.KnownIPNetworks);
    }

    [Fact]
    public void AddProxyForwarding_WithExplicitTrustBoundary_ConfiguresOnlyRequestedHop()
    {
        var options = ResolveOptions(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:ForwardLimit"] = "1",
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.10",
            ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/12"
        });

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Single(options.KnownProxies);
        Assert.Single(options.KnownIPNetworks);
        Assert.Contains(IPAddress.Parse("10.0.0.10"), options.KnownProxies);
        Assert.Contains(System.Net.IPNetwork.Parse("172.16.0.0/12"), options.KnownIPNetworks);
    }

    [Fact]
    public void AddProxyForwarding_RejectsUnrestrictedProxyTrust()
    {
        Assert.Throws<InvalidOperationException>(() => ResolveOptions(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:TrustAnyImmediateProxy"] = "true"
        }));
    }

    private static ForwardedHeadersOptions ResolveOptions(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddProxyForwarding(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    [Fact]
    public void AzureIngress_PreservesStandardTrustListsAndDisablesStandardHeaderProcessing()
    {
        var options = ResolveOptions(AzureValues());
        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
        Assert.NotEmpty(options.KnownProxies);
        Assert.NotEmpty(options.KnownIPNetworks);
    }

    [Theory]
    [InlineData("CONTAINER_APP_NAME", null)]
    [InlineData("CONTAINER_APP_NAME", "other-app")]
    [InlineData("CONTAINER_APP_REVISION", "other-app--revision")]
    [InlineData("CONTAINER_APP_ENV_DNS_SUFFIX", "other.azurecontainerapps.io")]
    [InlineData("ForwardedHeaders:Enabled", "false")]
    [InlineData("ForwardedHeaders:TrustAnyImmediateProxy", "true")]
    [InlineData("ForwardedHeaders:ForwardLimit", "2")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "10.0.0.1")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/24")]
    public void AzureIngress_RejectsWrongPlatformOrConflictingPolicy(string key, string? value)
    {
        var values = AzureValues();
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() => ResolveOptions(values));
    }

    internal static Dictionary<string, string?> AzureValues() => new()
    {
        ["ForwardedHeaders:Enabled"] = "true",
        ["ForwardedHeaders:ForwardLimit"] = "1",
        ["ForwardedHeaders:AzureContainerAppsIngress"] = "true",
        ["ForwardedHeaders:AzureContainerAppName"] = "hemodinks-api-prod",
        ["ForwardedHeaders:AzureContainerAppDnsSuffix"] = "test.brazilsouth.azurecontainerapps.io",
        ["CONTAINER_APP_NAME"] = "hemodinks-api-prod",
        ["CONTAINER_APP_REVISION"] = "hemodinks-api-prod--revision",
        ["CONTAINER_APP_ENV_DNS_SUFFIX"] = "test.brazilsouth.azurecontainerapps.io"
    };
}
