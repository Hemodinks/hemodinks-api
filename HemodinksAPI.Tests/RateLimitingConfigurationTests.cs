using System.Net;
using System.Threading.RateLimiting;
using HemodinksAPI.Api;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed class RateLimitingConfigurationTests
{
    [Theory]
    [InlineData("person@example.invalid", " PERSON@example.invalid ")]
    [InlineData("a@example.invalid", "A@example.invalid")]
    public void SubjectKey_NormalizesEmailWithoutExposingIt(string first, string second)
    {
        using var limits = new AuthenticationRateLimits(Options.Create(new AuthenticationRateLimitOptions()));
        var key = limits.SubjectKey("login", first, email: true);
        Assert.Equal(key, limits.SubjectKey("login", second, email: true));
        Assert.Equal(70, key.Length);
        Assert.DoesNotContain(first, key);
        Assert.NotEqual(key, limits.SubjectKey("recovery", first, email: true));
    }

    [Fact]
    public void SubjectKey_BoundsUntrustedInput()
    {
        using var limits = new AuthenticationRateLimits(Options.Create(new AuthenticationRateLimitOptions()));
        Assert.Equal(limits.SubjectKey("login", new string('a', 513)), limits.SubjectKey("login", new string('b', 514)));
    }

    [Fact]
    public void OriginBudget_NormalizesMappedAddressAndSeparatesOrigins()
    {
        using var limits = new AuthenticationRateLimits(Options.Create(new AuthenticationRateLimitOptions { IpPermitLimit = 1 }));
        using var first = limits.AcquireOrigin(IPAddress.Parse("203.0.113.10"));
        using var mapped = limits.AcquireOrigin(IPAddress.Parse("::ffff:203.0.113.10"));
        using var other = limits.AcquireOrigin(IPAddress.Parse("203.0.113.11"));
        Assert.True(first.IsAcquired);
        Assert.False(mapped.IsAcquired);
        Assert.True(mapped.TryGetMetadata(MetadataName.RetryAfter, out var retry));
        Assert.True(retry > TimeSpan.Zero);
        Assert.True(other.IsAcquired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100001)]
    public void Configuration_RejectsInvalidLimits(int limit)
    {
        Assert.False(new AuthenticationRateLimitOptions { IpPermitLimit = limit }.IsValid());
    }
}
