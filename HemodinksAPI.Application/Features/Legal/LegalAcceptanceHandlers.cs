using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Legal;

public sealed class GetLegalAcceptanceStatusQueryHandler(
    ILegalAcceptanceDbContext context,
    IClinicaContext clinicaContext)
    : IRequestHandler<GetLegalAcceptanceStatusQuery, LegalAcceptanceStatusResponse>
{
    public Task<LegalAcceptanceStatusResponse> Handle(
        GetLegalAcceptanceStatusQuery request,
        CancellationToken cancellationToken) =>
        ReadAsync(request, cancellationToken);

    private Task<LegalAcceptanceStatusResponse> ReadAsync(
        GetLegalAcceptanceStatusQuery request,
        CancellationToken cancellationToken)
    {
        LegalAcceptanceTenantGuard.EnsureCurrentClinic(clinicaContext, request.CurrentUser);
        return LegalAcceptanceStatusReader.ReadAsync(context, request.CurrentUser, cancellationToken);
    }
}

public sealed class AcceptCurrentLegalDocumentsCommandHandler(
    ILegalAcceptanceDbContext context,
    IClinicaContext clinicaContext,
    TimeProvider timeProvider)
    : IRequestHandler<AcceptCurrentLegalDocumentsCommand, LegalAcceptanceStatusResponse>
{
    public async Task<LegalAcceptanceStatusResponse> Handle(
        AcceptCurrentLegalDocumentsCommand request,
        CancellationToken cancellationToken)
    {
        LegalAcceptanceTenantGuard.EnsureCurrentClinic(clinicaContext, request.CurrentUser);

        if (!string.Equals(request.TermsOfUseVersion, LegalDocumentVersions.TermsOfUse, StringComparison.Ordinal)
            || !string.Equals(request.PrivacyNoticeVersion, LegalDocumentVersions.PrivacyNotice, StringComparison.Ordinal))
        {
            throw new ArgumentException("As versões dos documentos jurídicos não correspondem às versões vigentes.");
        }

        var acceptedTypes = await context.UserLegalAcceptances
            .Where(item => item.UserId == request.CurrentUser.Id
                && item.ClinicaId == request.CurrentUser.ClinicaId
                && ((item.DocumentType == LegalDocumentType.TermsOfUse
                        && item.DocumentVersion == LegalDocumentVersions.TermsOfUse)
                    || (item.DocumentType == LegalDocumentType.PrivacyNoticeAcknowledgement
                        && item.DocumentVersion == LegalDocumentVersions.PrivacyNotice)))
            .Select(item => item.DocumentType)
            .ToListAsync(cancellationToken);

        var acceptedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        AddIfMissing(
            acceptedTypes,
            LegalDocumentType.TermsOfUse,
            LegalDocumentVersions.TermsOfUse,
            request.CurrentUser,
            acceptedAtUtc);
        AddIfMissing(
            acceptedTypes,
            LegalDocumentType.PrivacyNoticeAcknowledgement,
            LegalDocumentVersions.PrivacyNotice,
            request.CurrentUser,
            acceptedAtUtc);

        await context.SaveChangesAsync(cancellationToken);
        return await LegalAcceptanceStatusReader.ReadAsync(context, request.CurrentUser, cancellationToken);
    }

    private void AddIfMissing(
        IReadOnlyCollection<LegalDocumentType> acceptedTypes,
        LegalDocumentType documentType,
        string documentVersion,
        CurrentUserContext currentUser,
        DateTime acceptedAtUtc)
    {
        if (acceptedTypes.Contains(documentType)) return;

        context.UserLegalAcceptances.Add(new UserLegalAcceptance
        {
            UserId = currentUser.Id,
            ClinicaId = currentUser.ClinicaId,
            DocumentType = documentType,
            DocumentVersion = documentVersion,
            AcceptedAtUtc = acceptedAtUtc
        });
    }
}

internal static class LegalAcceptanceTenantGuard
{
    public static void EnsureCurrentClinic(IClinicaContext clinicaContext, CurrentUserContext currentUser)
    {
        if (clinicaContext.GetRequiredClinicaId() != currentUser.ClinicaId)
        {
            throw new UnauthorizedAccessException("Usuário autenticado não pertence à clínica resolvida.");
        }
    }
}

internal static class LegalAcceptanceStatusReader
{
    public static async Task<LegalAcceptanceStatusResponse> ReadAsync(
        ILegalAcceptanceDbContext context,
        CurrentUserContext currentUser,
        CancellationToken cancellationToken)
    {
        var records = await context.UserLegalAcceptances
            .AsNoTracking()
            .Where(item => item.UserId == currentUser.Id && item.ClinicaId == currentUser.ClinicaId)
            .OrderByDescending(item => item.AcceptedAtUtc)
            .Select(item => new { item.DocumentType, item.DocumentVersion, item.AcceptedAtUtc })
            .ToListAsync(cancellationToken);

        var terms = BuildStatus(
            records.Where(item => item.DocumentType == LegalDocumentType.TermsOfUse)
                .Select(item => (item.DocumentVersion, item.AcceptedAtUtc)),
            nameof(LegalDocumentType.TermsOfUse),
            LegalDocumentVersions.TermsOfUse);
        var privacy = BuildStatus(
            records.Where(item => item.DocumentType == LegalDocumentType.PrivacyNoticeAcknowledgement)
                .Select(item => (item.DocumentVersion, item.AcceptedAtUtc)),
            nameof(LegalDocumentType.PrivacyNoticeAcknowledgement),
            LegalDocumentVersions.PrivacyNotice);

        return new LegalAcceptanceStatusResponse(!terms.IsCurrent || !privacy.IsCurrent, terms, privacy);
    }

    private static LegalDocumentAcceptanceStatus BuildStatus(
        IEnumerable<(string Version, DateTime AcceptedAtUtc)> records,
        string documentType,
        string currentVersion)
    {
        var latest = records.FirstOrDefault();
        var hasAcceptance = !string.IsNullOrWhiteSpace(latest.Version);
        return new LegalDocumentAcceptanceStatus(
            documentType,
            currentVersion,
            hasAcceptance ? latest.Version : null,
            hasAcceptance ? latest.AcceptedAtUtc : null,
            hasAcceptance && string.Equals(latest.Version, currentVersion, StringComparison.Ordinal));
    }
}
