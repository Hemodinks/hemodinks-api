using HemodinksAPI.Application.Features.Clinics.Platform;

namespace HemodinksAPI.Api;

public static partial class ClinicaPlatformEndpointExtensions
{
    public static void MapClinicaPlatformEndpoints(this WebApplication app)
    {
        var group = MapClinicPlatformGroup(app);
        MapClinicCrudEndpoints(group);
        MapClinicTeamEndpoints(group);
        MapPlatformAuditEndpoints(app);
    }

    private static Task<IResult> ListClinicas(
        HttpContext httpContext,
        ClinicaPlatformRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.ListClinicas(httpContext.ToPlatformRequestContext(), cancellationToken).ToHttpResultAsync();

    private static Task<IResult> GetClinica(
        int id,
        ClinicaPlatformRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.GetClinica(id, cancellationToken).ToHttpResultAsync();

    private static Task<IResult> CreateClinica(
        CreateClinicaRequest request,
        HttpContext httpContext,
        ClinicaPlatformRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.CreateClinica(request, httpContext.ToPlatformRequestContext(), cancellationToken).ToHttpResultAsync();

    private static Task<IResult> UpdateClinica(
        int id,
        UpdateClinicaRequest request,
        HttpContext httpContext,
        ClinicaPlatformRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.UpdateClinica(id, request, httpContext.ToPlatformRequestContext(), cancellationToken).ToHttpResultAsync();

    private static Task<IResult> DeactivateClinica(
        int id,
        HttpContext httpContext,
        ClinicaPlatformRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.DeactivateClinica(id, httpContext.ToPlatformRequestContext(), cancellationToken).ToHttpResultAsync();

    private static Task<IResult> ListPlatformAudit(
        ClinicaPlatformRequestHandler handler,
        int? clinicaId,
        string? acao,
        DateTime? de,
        DateTime? ate,
        int pagina = 1,
        int tamanhoPagina = 50,
        CancellationToken cancellationToken = default) =>
        handler.ListPlatformAudit(clinicaId, acao, de, ate, pagina, tamanhoPagina, cancellationToken).ToHttpResultAsync();
}
