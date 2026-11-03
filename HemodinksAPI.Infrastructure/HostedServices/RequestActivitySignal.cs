namespace HemodinksAPI.Infrastructure.HostedServices;

// Last authenticated request seen by this replica. Background polling uses it so a replica
// without users (scheduled start, probes, anonymous traffic) does not resume a paused
// serverless database. Holds only a timestamp: no user, tenant or request data.
public sealed class RequestActivitySignal(TimeProvider timeProvider)
{
    private long _lastActivityTicks;

    public void MarkActive() =>
        Interlocked.Exchange(ref _lastActivityTicks, timeProvider.GetUtcNow().UtcTicks);

    public bool IsActiveWithin(TimeSpan window)
    {
        var last = Interlocked.Read(ref _lastActivityTicks);
        return last != 0 && timeProvider.GetUtcNow().UtcTicks - last <= window.Ticks;
    }
}

public sealed record EventEmailPollingOptions(bool RequireRecentAuthenticatedRequest, TimeSpan RecentRequestWindow)
{
    public static readonly EventEmailPollingOptions Default = new(true, TimeSpan.FromMinutes(15));
}
