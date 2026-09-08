namespace HemodinksAPI.Domain.Models;

public enum LegalDocumentType
{
    TermsOfUse = 1,
    PrivacyNoticeAcknowledgement = 2
}

public sealed class UserLegalAcceptance : IClinicaOwnedEntity
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public int ClinicaId { get; set; }

    public Clinica Clinica { get; set; } = null!;

    public LegalDocumentType DocumentType { get; set; }

    public string DocumentVersion { get; set; } = null!;

    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;
}
