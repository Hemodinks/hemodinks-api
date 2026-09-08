using HemodinksAPI.Application.Authorization;
using MediatR;

namespace HemodinksAPI.Application.Features.Legal;

public static class LegalDocumentVersions
{
    public const string TermsOfUse = "1.1";
    public const string PrivacyNotice = "1.1";
}

public sealed record LegalDocumentAcceptanceStatus(
    string DocumentType,
    string CurrentVersion,
    string? AcceptedVersion,
    DateTime? AcceptedAtUtc,
    bool IsCurrent);

public sealed record LegalAcceptanceStatusResponse(
    bool RequiresAcceptance,
    LegalDocumentAcceptanceStatus TermsOfUse,
    LegalDocumentAcceptanceStatus PrivacyNotice);

public sealed record GetLegalAcceptanceStatusQuery(CurrentUserContext CurrentUser)
    : IRequest<LegalAcceptanceStatusResponse>;

public sealed record AcceptCurrentLegalDocumentsCommand(
    CurrentUserContext CurrentUser,
    string TermsOfUseVersion,
    string PrivacyNoticeVersion)
    : IRequest<LegalAcceptanceStatusResponse>;
