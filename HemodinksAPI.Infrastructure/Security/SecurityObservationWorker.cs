using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using HemodinksAPI.Application.Security;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;

namespace HemodinksAPI.Infrastructure.Security;

public sealed class SecurityObservationWorker : BackgroundService, ISecurityObservationWriter
{
    private readonly Channel<SecurityObservation> _queue;
    private readonly SecurityPatternDetector _detector;
    private readonly ILogger<SecurityObservationWorker> _logger;
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly TimeProvider _clock;
    private readonly Meter _meter = new("Hemodinks.Security");
    private readonly Counter<long> _dropped;
    private readonly Counter<long> _events;
    public const int RetentionDays = 30;
    public const int MaximumFiles = 30;
    public const long MaximumFileBytes = 10 * 1024 * 1024;
    public const string FilePattern = "hemodinks-security-.json";

    public SecurityObservationWorker(IOptions<SecurityObservationOptions> options,
        ILogger<SecurityObservationWorker> logger, TimeProvider clock)
    {
        var settings = options.Value;
        _queue = Channel.CreateBounded<SecurityObservation>(new BoundedChannelOptions(settings.QueueCapacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _detector = new(settings);
        _logger = logger;
        _clock = clock;
        _dropped = _meter.CreateCounter<long>("hemodinks.security.observation.dropped");
        _events = _meter.CreateCounter<long>("hemodinks.security.observation.events");
    }

    public string? PseudonymizeAccount(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return null;
        // A dedicated ephemeral key, daily namespace and purpose separation prevent dictionary attacks
        // and indefinite correlation. Never reuse JWT/encryption secrets for telemetry.
        var input = _clock.GetUtcNow().ToString("yyyyMMdd") + ":account:" + email.Trim().ToLowerInvariant();
        return Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(input)));
    }

    public bool TryWrite(SecurityObservation observation)
    {
        // Whitelist every text field at the persistence boundary, even for future callers.
        if (!Enum.IsDefined(observation.Kind)) return false;
        observation = observation with
        {
            Operation = observation.Operation is "login" or "discovery" or "operator" or "recovery" or "recovery_confirm"
                or "credential_change" or "credential_revoke" or "bootstrap" or "authorization" or "session" or "other" or "detection" ? observation.Operation : "other",
            Reason = observation.Reason is "invalid_credential" or "authorization_denied" or "infrastructure_failure"
                or "temporary_conflict" or "session_validation_busy" or "session_refresh_conflict" or "absolute_expired" or "idle_expired" or "session_invalid" or "rate_limited"
                or "invalid_request" or "completed" ? observation.Reason : "unspecified",
            RequestId = new string(observation.RequestId.Take(100).Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':').ToArray()),
            AccountKey = observation.AccountKey is { Length: 64 } key && key.All(char.IsAsciiHexDigit) ? key : null,
            ClinicId = observation.ClinicId > 0 ? observation.ClinicId : null
        };
        var accepted = _queue.Writer.TryWrite(observation);
        if (!accepted) _dropped.Add(1);
        return accepted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var value in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    _events.Add(1, new KeyValuePair<string, object?>("event", value.Kind.ToString()));
                    if (value.Kind != SecurityEventKind.SessionValidated) Persist(value);
                    foreach (var alert in _detector.Observe(value)) Persist(alert);
                }
                catch (Exception) { _dropped.Add(1); } // Never forward exception payloads or affect authentication.
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private void Persist(SecurityObservation value) => _logger.LogInformation(new EventId(4200, "SecurityObservation"),
        "Security {SecurityEvent} {Operation} {Reason}; request {RequestId}; clinic {ClinicId}; account {AccountKey}; at {ObservedAt}",
        value.Kind.ToString(), value.Operation, value.Reason, value.RequestId, value.ClinicId, value.AccountKey, value.Timestamp);

    public override void Dispose() { _queue.Writer.TryComplete(); _meter.Dispose(); CryptographicOperations.ZeroMemory(_key); base.Dispose(); }
}
