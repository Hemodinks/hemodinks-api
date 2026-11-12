using HemodinksAPI.Application.Features.Sessions;

namespace HemodinksAPI.Api;

public static partial class SessionEndpointExtensions
{
    private static async Task<IResult> RestoreSession(HttpContext context, AuthenticationSessionService sessions,
        AuthenticationSessionCookie cookie, IConfiguration configuration, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!IsTrustedRefreshRequest(context, configuration)) return Results.StatusCode(403);
        var token = cookie.Read(context);
        if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
        try
        {
            // No identity, clinic, or activity timestamp from the browser authorizes restoration.
            var issued = await sessions.RefreshAsync(token, ct);
            if (issued == null) return Results.Unauthorized();
            context.Items[SecurityObservationMiddleware.IssuedClinic] = issued.Identity.ClinicaId;
            cookie.Write(context, issued);
            return Results.Ok(issued.Identity);
        }
        catch (SessionRefreshConflictException) { return Results.Conflict(new { code = "session_refresh_conflict" }); }
        catch (SessionAbsoluteExpiredException)
        {
            context.Items[SecurityObservationMiddleware.Failure] = SessionLifetimePolicy.AbsoluteExpired;
            return Results.Json(new { code = SessionLifetimePolicy.AbsoluteExpired,
                message = SessionLifetimePolicy.AbsoluteExpiredMessage }, statusCode: 401);
        }
    }
}
