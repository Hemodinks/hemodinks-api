namespace HemodinksAPI.Application.Security;

// Called exclusively by the background consumer. State is bounded and local to this process.
public sealed class SecurityPatternDetector(SecurityObservationOptions options)
{
    private readonly Queue<SecurityObservation> _window = new();
    private readonly Dictionary<string, DateTimeOffset> _lastAlert = new();
    private DateTimeOffset _nextCleanup;

    public IReadOnlyList<SecurityObservation> Observe(SecurityObservation value)
    {
        var cutoff = value.Timestamp.AddMinutes(-options.WindowMinutes);
        while (_window.TryPeek(out var oldest) && (oldest.Timestamp < cutoff || _window.Count >= options.MaximumWindowEvents))
            _window.Dequeue();
        if (value.Timestamp >= _nextCleanup)
        {
            foreach (var key in _lastAlert.Where(x => x.Value < value.Timestamp.AddMinutes(-options.AlertCooldownMinutes)).Select(x => x.Key).ToArray())
                _lastAlert.Remove(key);
            _nextCleanup = value.Timestamp.AddMinutes(1);
        }
        _window.Enqueue(value);
        var alerts = new List<SecurityObservation>();
        if (IsCredentialFailure(value))
        {
            var failures = _window.Where(IsCredentialFailure).ToArray();
            if (failures.Count(x => x.AccountKey == value.AccountKey) >= options.AccountFailureThreshold)
                Alert("account_failures", value.AccountKey);
            var attempts = _window.Count(x => IsCredentialFailure(x) || x.Kind is SecurityEventKind.AuthenticationSucceeded or SecurityEventKind.CredentialValidated);
            if (failures.Length >= options.MultipleAccountFailuresThreshold
                && failures.Select(x => x.AccountKey).Distinct().Count() >= options.MultipleAccountsThreshold
                && (double)failures.Length / attempts >= options.AuthenticationFailureRatio)
                Alert("multiple_accounts", null);
        }
        if (IsSessionError(value))
        {
            // Compare only session outcomes; protected business authorization failures are excluded.
            var sessions = _window.Where(x => x.Kind == SecurityEventKind.SessionValidated || IsSessionError(x)).ToArray();
            var errors = sessions.Count(IsSessionError);
            if (sessions.Length >= options.SessionMinimumSamples && errors >= options.SessionErrorThreshold
                && (double)errors / sessions.Length >= options.SessionErrorRatio)
                Alert("session_error_growth", null);
        }
        return alerts;

        void Alert(string rule, string? account)
        {
            var key = rule + account;
            if (_lastAlert.TryGetValue(key, out var previous)
                && value.Timestamp - previous < TimeSpan.FromMinutes(options.AlertCooldownMinutes)) return;
            if (!_lastAlert.ContainsKey(key) && _lastAlert.Count >= options.MaximumWindowEvents) return;
            _lastAlert[key] = value.Timestamp;
            // Cross-account and session-wide patterns are platform-only, never assigned to a tenant.
            alerts.Add(new(value.Timestamp, SecurityEventKind.SuspiciousPattern, "detection", rule,
                value.RequestId, AccountKey: account));
        }
    }

    private static bool IsCredentialFailure(SecurityObservation x) =>
        x.Kind == SecurityEventKind.AuthenticationRefused && x.Reason == "invalid_credential" && x.AccountKey != null;
    private static bool IsSessionError(SecurityObservation x) =>
        x.Operation == "session" && (x.Kind is SecurityEventKind.SessionExpired or SecurityEventKind.SessionRejected
            or SecurityEventKind.TemporaryConflict or SecurityEventKind.InfrastructureFailure);
}
