namespace HemodinksAPI.Domain.Models;

public sealed class UserPrivacyPreference : IClinicaOwnedEntity
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public int ClinicaId { get; set; }

    public Clinica Clinica { get; set; } = null!;

    public string DocumentVersion { get; set; } = null!;

    public bool PreferencesEnabled { get; set; }

    public bool AnalyticsEnabled { get; set; }

    public DateTime AcceptedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
