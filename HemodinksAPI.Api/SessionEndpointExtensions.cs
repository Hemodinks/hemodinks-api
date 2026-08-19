using System.Security.Claims;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Application.Tenancy;

namespace HemodinksAPI.Api;

public static class SessionEndpointExtensions
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/session")
            .WithTags("Sessao")
            .RequireAuthorization();

        group.MapGet("/clinicas", ListClinicas);
        group.MapPost("/selecionar-clinica", SelectClinica);
    }

    private static async Task<IResult> ListClinicas(
        ClaimsPrincipal principal,
        SessionUseCases useCases,
        CancellationToken cancellationToken)
    {
        var currentUser = principal.ToCurrentUserContext();
        if (currentUser == null || currentUser.UsuarioGlobalId <= 0)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await useCases.ListClinicsAsync(currentUser, cancellationToken));
    }

    private static async Task<IResult> SelectClinica(
        SelectClinicRequest request,
        HttpContext httpContext,
        SessionUseCases useCases,
        AuthenticationSessionService sessionService,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var currentUser = httpContext.User.ToCurrentUserContext();
        if (request.ClinicaId <= 0 || currentUser == null || currentUser.UsuarioGlobalId <= 0)
        {
            return Results.BadRequest(new { message = "ClinicaId invalido ou identidade global ausente." });
        }

        var sessionId = Guid.TryParse(
            httpContext.User.FindFirstValue(AuthenticationSessionClaimTypes.SessionId),
            out var parsedSessionId)
            ? parsedSessionId
            : (Guid?)null;
        var result = await useCases.SelectClinicAsync(
            request.ClinicaId,
            currentUser,
            sessionId,
            cancellationToken);
        if (result == null)
        {
            await auditService.RecordAsync(httpContext, "session.clinic.switch.denied", "session",
                request.ClinicaId.ToString(), null, new { requestedClinicId = request.ClinicaId }, false,
                cancellationToken);
            return Results.Forbid();
        }

        if (sessionId.HasValue
            && !await sessionService.ChangeMembershipAsync(
                sessionId.Value,
                result.Clinica.UsuarioClinicaId,
                cancellationToken))
        {
            return Results.Unauthorized();
        }

        var previousClinicId = int.TryParse(
            httpContext.User.FindFirstValue(ClinicaClaimTypes.ClinicaId), out var parsedClinicId)
            ? parsedClinicId
            : (int?)null;

        await auditService.RecordAsync(httpContext, "session.clinic.switch", "session",
            result.Clinica.UsuarioClinicaId.ToString(), result.Clinica.ClinicaId,
            new { previousClinicId, selectedClinicId = result.Clinica.ClinicaId }, true, cancellationToken);

        return Results.Ok(result);
    }

    public sealed record SelectClinicRequest(int ClinicaId);
}
