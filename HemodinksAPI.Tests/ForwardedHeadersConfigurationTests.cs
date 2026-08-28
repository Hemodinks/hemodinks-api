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
    public void AddProxyForwarding_WhenImmediateProxyIsIsolated_AllowsExplicitPlatformTrust()
    {
        var options = ResolveOptions(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:TrustAnyImmediateProxy"] = "true"
        });

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
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
}
