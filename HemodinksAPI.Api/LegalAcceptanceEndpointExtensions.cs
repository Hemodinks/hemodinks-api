using HemodinksAPI.Application.Features.Legal;
using MediatR;

namespace HemodinksAPI.Api;

public static class LegalAcceptanceEndpointExtensions
{
    public static void MapLegalAcceptanceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/legal-acceptances")
            .WithTags("Legal")
            .RequireAuthorization();

        group.MapGet("/current", GetCurrent)
            .WithName("GetCurrentLegalAcceptances")
            .WithSummary("Consultar aceites jurídicos vigentes do usuário na clínica atual");

        group.MapPost("/current", AcceptCurrent)
            .WithName("AcceptCurrentLegalDocuments")
            .WithSummary("Registrar aceite dos documentos jurídicos vigentes");
    }

    private static async Task<IResult> GetCurrent(
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var currentUser = httpContext.User.ToCurrentUserContext();
        if (currentUser == null) return Results.Unauthorized();

        var result = await mediator.Send(
            new GetLegalAcceptanceStatusQuery(currentUser),
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> AcceptCurrent(
        AcceptLegalDocumentsRequest request,
        HttpContext httpContext,
        IMediator mediator,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var currentUser = httpContext.User.ToCurrentUserContext();
        if (currentUser == null) return Results.Unauthorized();

        if (!string.Equals(request.TermsOfUseVersion, LegalDocumentVersions.TermsOfUse, StringComparison.Ordinal)
            || !string.Equals(request.PrivacyNoticeVersion, LegalDocumentVersions.PrivacyNotice, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                message = "As versões informadas não correspondem aos documentos jurídicos vigentes. Atualize a página e tente novamente."
            });
        }

        var result = await mediator.Send(
            new AcceptCurrentLegalDocumentsCommand(
                currentUser,
                request.TermsOfUseVersion,
                request.PrivacyNoticeVersion),
            cancellationToken);

        await auditService.RecordAsync(
            httpContext,
            "legal.accept",
            "legal-documents",
            currentUser.Id.ToString(),
            currentUser.ClinicaId,
            new
            {
                termsOfUseVersion = request.TermsOfUseVersion,
                privacyNoticeVersion = request.PrivacyNoticeVersion
            },
            true,
            cancellationToken);

        return Results.Ok(result);
    }

    public sealed record AcceptLegalDocumentsRequest(
        string TermsOfUseVersion,
        string PrivacyNoticeVersion);
}
