using HemodinksAPI.Application.Authorization;
using MediatR;

namespace HemodinksAPI.Application.Features.Privacy;

public static class PrivacyPolicyVersions
{
    public const string Current = "1.1";
}

public sealed record PrivacyPreferenceResponse(
    bool HasPreference,
    string CurrentDocumentVersion,
    string? DocumentVersion,
    bool PreferencesEnabled,
    bool AnalyticsEnabled,
    DateTime? AcceptedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record GetCurrentPrivacyPreferenceQuery(CurrentUserContext CurrentUser)
    : IRequest<PrivacyPreferenceResponse>;

public sealed record UpdateCurrentPrivacyPreferenceCommand(
    CurrentUserContext CurrentUser,
    string DocumentVersion,
    bool PreferencesEnabled,
    bool AnalyticsEnabled)
    : IRequest<PrivacyPreferenceResponse>;
