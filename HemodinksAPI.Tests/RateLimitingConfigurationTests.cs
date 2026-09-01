using System.Net;
using HemodinksAPI.Api;

namespace HemodinksAPI.Tests;

public sealed class RateLimitingConfigurationTests
{
    [Theory]
    [InlineData("hemodinks", "HEMODINKS")]
    [InlineData("Hemodinks", "  hemodinks  ")]
    [InlineData("clinica-central", "CLINICA-CENTRAL")]
    public void LoginPartition_NormalizesEquivalentClinicSlugs(string firstSlug, string secondSlug)
    {
        var address = IPAddress.Parse("203.0.113.10");

        var firstKey = ApiServiceCollectionExtensions.BuildLoginRateLimitPartitionKey(address, firstSlug);
        var secondKey = ApiServiceCollectionExtensions.BuildLoginRateLimitPartitionKey(address, secondSlug);

        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public void LoginPartition_BoundsUntrustedClinicSlug()
    {
        var key = ApiServiceCollectionExtensions.BuildLoginRateLimitPartitionKey(
            IPAddress.Parse("203.0.113.10"),
            new string('A', 500));

        var normalizedSlug = key[(key.LastIndexOf(':') + 1)..];
        Assert.Equal(120, normalizedSlug.Length);
        Assert.Equal(new string('a', 120), normalizedSlug);
    }

    [Fact]
    public void LoginPartition_StillSeparatesClientAddresses()
    {
        var firstKey = ApiServiceCollectionExtensions.BuildLoginRateLimitPartitionKey(
            IPAddress.Parse("203.0.113.10"),
            "hemodinks");
        var secondKey = ApiServiceCollectionExtensions.BuildLoginRateLimitPartitionKey(
            IPAddress.Parse("203.0.113.11"),
            "hemodinks");

        Assert.NotEqual(firstKey, secondKey);
    }
}
