namespace HemodinksAPI.Application.Security;

// No arbitrary payload, credential, session identifier, IP or account identifier is accepted.
public enum SecurityEventKind
{
    AuthenticationSucceeded, AuthenticationRefused, CredentialValidated, Throttled,
    RecoveryRequested, RecoveryCompleted, RecoveryRefused, CredentialChanged, CredentialRevoked, SessionRevoked,
    TemporaryConflict, SessionExpired, SessionRejected, AuthorizationDenied,
    InfrastructureFailure, SessionValidated, SuspiciousPattern
}

public sealed record SecurityObservation(DateTimeOffset Timestamp, SecurityEventKind Kind,
    string Operation, string Reason, string RequestId, int? ClinicId = null,
    string? AccountKey = null);

public interface ISecurityObservationWriter
{
    string? PseudonymizeAccount(string? email);
    bool TryWrite(SecurityObservation observation);
}

public sealed class SecurityObservationOptions
{
    public const string SectionName = "SecurityObservation";
    public int QueueCapacity { get; set; } = 4096;
    public int WindowMinutes { get; set; } = 10;
    public int MaximumWindowEvents { get; set; } = 10000;
    public int AccountFailureThreshold { get; set; } = 10;
    public int MultipleAccountsThreshold { get; set; } = 20;
    public int MultipleAccountFailuresThreshold { get; set; } = 50;
    public double AuthenticationFailureRatio { get; set; } = 0.9;
    public int SessionMinimumSamples { get; set; } = 100;
    public int SessionErrorThreshold { get; set; } = 20;
    public double SessionErrorRatio { get; set; } = 0.2;
    public int AlertCooldownMinutes { get; set; } = 10;

    public bool IsValid() => QueueCapacity is >= 16 and <= 65536
        && WindowMinutes is >= 1 and <= 60 && MaximumWindowEvents is >= 100 and <= 100000
        && AccountFailureThreshold >= 2 && MultipleAccountsThreshold >= 2
        && MultipleAccountFailuresThreshold >= MultipleAccountsThreshold
        && AuthenticationFailureRatio is >= 0.5 and <= 1
        && SessionMinimumSamples >= 2 && SessionErrorThreshold >= 2
        && SessionErrorRatio is > 0 and <= 1 && AlertCooldownMinutes is >= 1 and <= 60;
}
