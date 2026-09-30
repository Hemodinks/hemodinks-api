using Hemodinks.SchemaGuard;

namespace HemodinksAPI.Tests;

public sealed class SqlReadRetryTests
{
    [Theory]
    [InlineData(-2, true)]
    [InlineData(40613, true)]
    [InlineData(40501, true)]
    [InlineData(18456, false)]
    [InlineData(229, false)]
    [InlineData(40615, false)]
    [InlineData(4060, false)]
    public void OnlyTransientSqlFailuresAreRetried(int number, bool expected) =>
        Assert.Equal(expected, SqlReadRetry.IsTransientSqlNumber(number));

    [Fact]
    public async Task TransientFailureCanRecover()
    {
        var attempts = 0;
        var waits = 0;
        var result = await SqlReadRetry.ExecuteAsync(_ =>
        {
            if (++attempts < 3) throw new TimeoutException();
            return Task.FromResult(true);
        }, CancellationToken.None, delay: (duration, _) =>
        {
            Assert.Equal(TimeSpan.FromSeconds(10), duration);
            waits++;
            return Task.CompletedTask;
        });
        Assert.True(result);
        Assert.Equal(3, attempts);
        Assert.Equal(2, waits);
    }

    [Fact]
    public async Task ExhaustedTimeoutStillBlocksDeployment()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<TimeoutException>(() => SqlReadRetry.ExecuteAsync(_ =>
        {
            attempts++;
            throw new TimeoutException();
        }, CancellationToken.None, delay: (_, _) => Task.CompletedTask));
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task SchemaMismatchIsNeverRetriedOrAccepted()
    {
        var attempts = 0;
        Assert.False(await SqlReadRetry.ExecuteAsync(_ =>
        {
            attempts++;
            return Task.FromResult(false);
        }, CancellationToken.None));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ConfigurationFailureIsNotRetried()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<ArgumentException>(() => SqlReadRetry.ExecuteAsync(_ =>
        {
            attempts++;
            throw new ArgumentException();
        }, CancellationToken.None));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task CancellationDuringBackoffPreventsAnotherConnectionAttempt()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SqlReadRetry.ExecuteAsync(_ =>
        {
            attempts++;
            throw new TimeoutException();
        }, cancellation.Token, delay: (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }));
        Assert.Equal(1, attempts);
    }
}
