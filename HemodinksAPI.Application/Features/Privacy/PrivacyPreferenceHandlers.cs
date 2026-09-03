using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Privacy;

public sealed class GetCurrentPrivacyPreferenceQueryHandler(
    IPrivacyPreferenceDbContext context,
    IClinicaContext clinicaContext)
    : IRequestHandler<GetCurrentPrivacyPreferenceQuery, PrivacyPreferenceResponse>
{
    public async Task<PrivacyPreferenceResponse> Handle(
        GetCurrentPrivacyPreferenceQuery request,
        CancellationToken cancellationToken)
    {
        PrivacyPreferenceTenantGuard.EnsureCurrentClinic(clinicaContext, request.CurrentUser);
        var preference = await FindAsync(context, request.CurrentUser, cancellationToken);
        return PrivacyPreferenceMapper.ToResponse(preference);
    }

    internal static Task<UserPrivacyPreference?> FindAsync(
        IPrivacyPreferenceDbContext context,
        CurrentUserContext currentUser,
        CancellationToken cancellationToken) =>
        context.UserPrivacyPreferences
            .SingleOrDefaultAsync(item =>
                item.UserId == currentUser.Id && item.ClinicaId == currentUser.ClinicaId,
                cancellationToken);
}

public sealed class UpdateCurrentPrivacyPreferenceCommandHandler(
    IPrivacyPreferenceDbContext context,
    IClinicaContext clinicaContext,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateCurrentPrivacyPreferenceCommand, PrivacyPreferenceResponse>
{
    public async Task<PrivacyPreferenceResponse> Handle(
        UpdateCurrentPrivacyPreferenceCommand request,
        CancellationToken cancellationToken)
    {
        PrivacyPreferenceTenantGuard.EnsureCurrentClinic(clinicaContext, request.CurrentUser);
        if (!string.Equals(request.DocumentVersion, PrivacyPolicyVersions.Current, StringComparison.Ordinal))
        {
            throw new ArgumentException("A versão da política de privacidade não corresponde à versão vigente.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var preference = await GetCurrentPrivacyPreferenceQueryHandler.FindAsync(
            context,
            request.CurrentUser,
            cancellationToken);

        if (preference is null)
        {
            preference = new UserPrivacyPreference
            {
                UserId = request.CurrentUser.Id,
                ClinicaId = request.CurrentUser.ClinicaId,
                DocumentVersion = request.DocumentVersion,
                AcceptedAtUtc = nowUtc
            };
            context.UserPrivacyPreferences.Add(preference);
        }
        else if (!string.Equals(preference.DocumentVersion, request.DocumentVersion, StringComparison.Ordinal))
        {
            preference.DocumentVersion = request.DocumentVersion;
            preference.AcceptedAtUtc = nowUtc;
        }

        preference.PreferencesEnabled = request.PreferencesEnabled;
        preference.AnalyticsEnabled = request.AnalyticsEnabled;
        preference.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);
        return PrivacyPreferenceMapper.ToResponse(preference);
    }
}

internal static class PrivacyPreferenceMapper
{
    public static PrivacyPreferenceResponse ToResponse(UserPrivacyPreference? preference) =>
        new(
            preference is not null,
            PrivacyPolicyVersions.Current,
            preference?.DocumentVersion,
            preference?.PreferencesEnabled ?? false,
            preference?.AnalyticsEnabled ?? false,
            preference?.AcceptedAtUtc,
            preference?.UpdatedAtUtc);
}

internal static class PrivacyPreferenceTenantGuard
{
    public static void EnsureCurrentClinic(IClinicaContext clinicaContext, CurrentUserContext currentUser)
    {
        if (clinicaContext.GetRequiredClinicaId() != currentUser.ClinicaId)
        {
            throw new UnauthorizedAccessException("Usuário autenticado não pertence à clínica resolvida.");
        }
    }
}
