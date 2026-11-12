using HemodinksAPI.Application.Features.Privacy;
using MediatR;

namespace HemodinksAPI.Api;

public static class PrivacyPreferenceEndpointExtensions
{
    public static void MapPrivacyPreferenceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/privacy-preferences")
            .WithTags("Privacy")
            .RequireAuthorization();

        group.MapGet("/current", GetCurrent)
            .WithName("GetCurrentPrivacyPreference")
            .WithSummary("Consultar as preferências de privacidade do usuário na clínica atual");

        group.MapPut("/current", UpdateCurrent)
            .WithName("UpdateCurrentPrivacyPreference")
            .WithSummary("Registrar as preferências de privacidade do usuário na clínica atual");
    }

    private static async Task<IResult> GetCurrent(
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var currentUser = httpContext.User.ToCurrentUserContext();
        if (currentUser is null) return Results.Unauthorized();

        return Results.Ok(await mediator.Send(
            new GetCurrentPrivacyPreferenceQuery(currentUser),
            cancellationToken));
    }

    private static async Task<IResult> UpdateCurrent(
        UpdatePrivacyPreferenceRequest request,
        HttpContext httpContext,
        IMediator mediator,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var currentUser = httpContext.User.ToCurrentUserContext();
        if (currentUser is null) return Results.Unauthorized();

        if (!string.Equals(request.DocumentVersion, PrivacyPolicyVersions.Current, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                message = "A versão informada não corresponde à política de privacidade vigente. Atualize a página e tente novamente."
            });
        }

        var result = await mediator.Send(
            new UpdateCurrentPrivacyPreferenceCommand(
                currentUser,
                request.DocumentVersion,
                request.PreferencesEnabled,
                request.AnalyticsEnabled),
            cancellationToken);

        await auditService.RecordAsync(
            httpContext,
            "privacy.preferences.update",
            "privacy-preference",
            currentUser.Id.ToString(),
            currentUser.ClinicaId,
            new
            {
                documentVersion = request.DocumentVersion,
                preferencesEnabled = request.PreferencesEnabled,
                analyticsEnabled = request.AnalyticsEnabled
            },
            true,
            cancellationToken);

        return Results.Ok(result);
    }

    public sealed record UpdatePrivacyPreferenceRequest(
        string DocumentVersion,
        bool PreferencesEnabled,
        bool AnalyticsEnabled);
}
