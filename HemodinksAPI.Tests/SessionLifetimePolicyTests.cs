using HemodinksAPI.Application.Features.Sessions;

namespace HemodinksAPI.Tests;

public sealed class SessionLifetimePolicyTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void AbsoluteDeadline_IsExclusiveForValidity(long ticks, bool expired)
    {
        var start = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var clock = new Clock(start.AddHours(12).AddTicks(ticks));
        var policy = new SessionLifetimePolicy(new(), clock);
        Assert.Equal(expired ? SessionLifetimePolicy.AbsoluteExpired : null,
            policy.Failure(start.UtcDateTime, clock.Now.UtcDateTime));
    }

    [Fact]
    public void EarlierIdleDeadlineWins_AndActivityCannotExtendAbsoluteDeadline()
    {
        var start = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var clock = new Clock(start.AddMinutes(30));
        var policy = new SessionLifetimePolicy(new(), clock);
        Assert.Equal(SessionLifetimePolicy.IdleExpired, policy.Failure(start.UtcDateTime, start.UtcDateTime));
        clock.Now = start.AddHours(12);
        Assert.Equal(SessionLifetimePolicy.IdleExpired, policy.Failure(start.UtcDateTime, start.UtcDateTime));
        Assert.Equal(SessionLifetimePolicy.AbsoluteExpired, policy.Failure(start.UtcDateTime, clock.Now.UtcDateTime));
    }

    [Theory]
    [InlineData(12, 30, 12)]
    [InlineData(72, 1, 24)]
    public void CookieHonorsEarliestDeadline(int absoluteHours, int cookieDays, int expectedHours)
    {
        var start = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var policy = new SessionLifetimePolicy(new() { AbsoluteLifetimeHours = absoluteHours,
            RefreshCookieLifetimeDays = cookieDays }, new Clock(start));
        Assert.Equal(start.UtcDateTime.AddHours(expectedHours), policy.CookieExpiresAt(start.UtcDateTime));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(8761)]
    [InlineData(int.MaxValue)]
    public void InvalidAbsoluteConfigurationIsRejected(int hours) => Assert.Throws<InvalidOperationException>(
        () => SessionLifetimePolicy.ValidateOptions(new() { AbsoluteLifetimeHours = hours }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("253402300800")]
    [InlineData("18446744073709551615")]
    [InlineData("tomorrow")]
    public void MissingOrMalformedAuthenticationTimeCannotStartNewWindow(string? claim)
    {
        Assert.Null(SessionLifetimePolicy.ParseAuthenticationTime(claim));
        Assert.Equal(SessionLifetimePolicy.ReauthenticationRequired,
            new SessionLifetimePolicy(new(), TimeProvider.System).Failure(null));
    }
}
