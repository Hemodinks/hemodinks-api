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
        group.MapPost("/renovar", RefreshSession).AllowAnonymous().RequireRateLimiting("SessionRefresh");
        group.MapPost("/renovar-equipe", RefreshTeamSession).WithName("RefreshTeamSession").RequireRateLimiting("SessionRefresh");
        group.MapPost("/sair", EndSession).AllowAnonymous().RequireRateLimiting("SessionRefresh");
        group.MapPost("/atividade", (AuthenticationSessionOptions options) =>
            Results.Ok(new { idleTimeoutMinutes = options.IdleTimeoutMinutes }))
            .WithName("TouchSessionActivity").RequireRateLimiting("SessionRefresh");
    }

    private static bool IsTrustedRefreshRequest(HttpContext context, IConfiguration configuration)
    {
        var origin = context.Request.Headers.Origin.ToString();
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return context.Request.Headers["X-Session-Refresh"] == "1"
            && (string.IsNullOrEmpty(origin) || origins.Any(item =>
                string.Equals(item.Trim().TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<IResult> EndSession(RefreshSessionRequest request, HttpContext context,
        AuthenticationSessionService sessions, AuthenticationSessionCookie cookie,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!IsTrustedRefreshRequest(context, configuration)) return Results.StatusCode(403);
        context.Response.Headers.CacheControl = "no-store";
        var token = cookie.Read(context);
        Guid? authenticatedId = context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(AuthenticationSessionClaimTypes.SessionId), out var sid) ? sid : null;
        int? authenticatedMembership = int.TryParse(context.User.FindFirstValue(GlobalIdentityClaimTypes.UsuarioClinicaId), out var member) ? member : null;
        if (await sessions.RevokeMatchingAsync(token ?? string.Empty, request.SessionId, request.MembershipId,
            cancellationToken, authenticatedId, authenticatedMembership))
            cookie.Delete(context);
        return Results.NoContent();
    }

    private static async Task<IResult> RefreshTeamSession(HttpContext context,
        HemodinksAPI.Application.Features.Teams.TeamUseCases teams,
        AuthenticationSessionOptions options, CancellationToken cancellationToken)
    {
        var user = context.User.ToCurrentUserContext();
        if (user == null || !int.TryParse(context.User.FindFirstValue(GlobalIdentityClaimTypes.EquipeVersaoSessao), out var teamVersion)
            || !Guid.TryParse(context.User.FindFirstValue("security_version"), out var securityVersion))
            return Results.Unauthorized();
        int? operatorVersion = int.TryParse(context.User.FindFirstValue(GlobalIdentityClaimTypes.OperadorVersaoSessao), out var version) ? version : null;
        var token = await teams.RenewSessionAsync(user, teamVersion, operatorVersion, securityVersion, cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return token == null ? Results.Unauthorized() : Results.Ok(new { token, idleTimeoutMinutes = options.IdleTimeoutMinutes });
    }

    private static async Task<IResult> RefreshSession(
        RefreshSessionRequest request, HttpContext context,
        AuthenticationSessionService sessions, AuthenticationSessionCookie cookie,
        AuthenticationSessionOptions options, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        // Custom header forces CORS preflight; also reject explicitly untrusted browser origins.
        if (!IsTrustedRefreshRequest(context, configuration))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        context.Response.Headers.CacheControl = "no-store";
        if (request.SessionId == Guid.Empty || request.MembershipId <= 0)
            return Results.Unauthorized();
        var token = cookie.Read(context);
        if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
        try
        {
            var issued = await sessions.RefreshAsync(token, cancellationToken,
                request.SessionId, request.MembershipId, request.Active);
            // Never delete a cookie on a failed refresh: another tab may have rotated it.
            if (issued == null) return Results.Unauthorized();
            cookie.Write(context, issued);
            return Results.Ok(new { token = issued.AccessToken, idleTimeoutMinutes = options.IdleTimeoutMinutes });
        }
        catch (SessionRefreshConflictException)
        {
            return Results.Conflict(new { code = "session_refresh_conflict" });
        }
    }

    public sealed record RefreshSessionRequest(Guid SessionId, int MembershipId, bool Active);

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
