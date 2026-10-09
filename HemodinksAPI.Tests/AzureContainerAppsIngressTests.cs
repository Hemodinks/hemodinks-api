using System.Net;
using HemodinksAPI.Api;
using Microsoft.AspNetCore.Http;

namespace HemodinksAPI.Tests;

public sealed class AzureContainerAppsIngressTests
{
    [Theory]
    [InlineData("spoofed, 198.51.100.5", "https", "198.51.100.5")]
    [InlineData("203.0.113.10, ::ffff:198.51.100.5", "http", "::ffff:198.51.100.5")]
    [InlineData("2001:db8::1", "https", "2001:db8::1")]
    public async Task OnlyIngressAppendedAddressAndProtocolAreUsed(string forwarded, string proto, string expected)
    {
        var context = Context(forwarded, proto);
        var called = false;
        await new AzureContainerAppsIngressMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);
        Assert.True(called);
        Assert.Equal(IPAddress.Parse(expected), context.Connection.RemoteIpAddress);
        Assert.Equal(proto, context.Request.Scheme);
    }

    [Theory]
    [InlineData("198.51.100.1, invalid", "https")]
    [InlineData("198.51.100.1,", "https")]
    [InlineData("fe80::1%eth0", "https")]
    [InlineData("198.51.100.1", "https,http")]
    [InlineData("198.51.100.1", "")]
    public async Task MalformedPlatformMetadataFailsBeforeDownstream(string forwarded, string proto)
    {
        var context = Context(forwarded, proto);
        await new AzureContainerAppsIngressMiddleware(_ => throw new InvalidOperationException("Downstream must not run.")).InvokeAsync(context);
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal(IPAddress.Loopback, context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task OversizedHeaderIsRejectedWithoutUsingClientPrefix()
    {
        var context = Context(new string('x', 8192) + ", 198.51.100.1", "https");
        await new AzureContainerAppsIngressMiddleware(_ => throw new InvalidOperationException()).InvokeAsync(context);
        Assert.Equal(400, context.Response.StatusCode);
    }

    [Fact]
    public async Task InternalProbeWithoutForwardingMetadataKeepsSocketIdentity()
    {
        var context = Context("", "");
        var called = false;
        await new AzureContainerAppsIngressMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);
        Assert.True(called);
        Assert.Equal(IPAddress.Loopback, context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    private static DefaultHttpContext Context(string forwarded, string proto)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = forwarded;
        context.Request.Headers["X-Forwarded-Proto"] = proto;
        return context;
    }
}
