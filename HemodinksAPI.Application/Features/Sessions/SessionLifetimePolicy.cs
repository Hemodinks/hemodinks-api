using System.Globalization;

namespace HemodinksAPI.Application.Features.Sessions;

public sealed class SessionLifetimePolicy(AuthenticationSessionOptions options, TimeProvider clock)
{
    public const string AbsoluteExpired = "session_absolute_expired";
    public const string IdleExpired = "session_idle_expired";
    public const string ReauthenticationRequired = "session_reauthentication_required";
    public const string AbsoluteExpiredMessage = "Sua sessão atingiu o tempo máximo. Entre novamente.";

    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public DateTime AbsoluteExpiresAt(DateTime startedAt) => startedAt.AddHours(options.AbsoluteLifetimeHours);
    public DateTime IdleExpiresAt(DateTime lastActivityAt) => lastActivityAt
        .AddMinutes(options.IdleTimeoutMinutes).AddSeconds(options.ActivityPersistenceIntervalSeconds);

    public string? Failure(DateTime? startedAt, DateTime? lastActivityAt = null)
    {
        var now = UtcNow;
        if (!startedAt.HasValue || startedAt.Value == default || startedAt.Value > now
            || startedAt.Value > DateTime.MaxValue.AddHours(-options.AbsoluteLifetimeHours))
            return ReauthenticationRequired;
        var absolute = AbsoluteExpiresAt(startedAt.Value);
        // Report the first deadline reached when both have elapsed.
        if (lastActivityAt.HasValue && now >= IdleExpiresAt(lastActivityAt.Value)
            && IdleExpiresAt(lastActivityAt.Value) < absolute)
            return IdleExpired;
        return now >= absolute ? AbsoluteExpired : null;
    }

    public DateTime CookieExpiresAt(DateTime startedAt) =>
        new(Math.Min(UtcNow.AddDays(options.RefreshCookieLifetimeDays).Ticks, AbsoluteExpiresAt(startedAt).Ticks), DateTimeKind.Utc);

    public static DateTime? ParseAuthenticationTime(string? value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds <= 0 || seconds > 253402300799) return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }

    public static void ValidateOptions(AuthenticationSessionOptions options)
    {
        if (options.RefreshCookieSameSite is not ("Lax" or "Strict" or "None"))
            throw new InvalidOperationException("AuthenticationSession:RefreshCookieSameSite must be Lax, Strict or None.");
        if (string.IsNullOrWhiteSpace(options.RefreshCookieName))
            throw new InvalidOperationException("AuthenticationSession:RefreshCookieName must be configured.");
        if (options.AbsoluteLifetimeHours is < 1 or > 8760)
            throw new InvalidOperationException("AuthenticationSession:AbsoluteLifetimeHours must be between 1 and 8760.");
        if (options.IdleTimeoutMinutes is < 1 or > 525600)
            throw new InvalidOperationException("AuthenticationSession:IdleTimeoutMinutes must be between 1 and 525600.");
        if (options.ActivityPersistenceIntervalSeconds < 0 || options.ActivityPersistenceIntervalSeconds > 60
            || options.ActivityPersistenceIntervalSeconds >= options.IdleTimeoutMinutes * 60)
            throw new InvalidOperationException("AuthenticationSession:ActivityPersistenceIntervalSeconds must be between 0 and 60 and less than the idle timeout.");
        if (options.RefreshCookieLifetimeDays is < 1 or > 365)
            throw new InvalidOperationException("AuthenticationSession:RefreshCookieLifetimeDays must be between 1 and 365.");
    }
}

public sealed class SessionAbsoluteExpiredException : Exception;
