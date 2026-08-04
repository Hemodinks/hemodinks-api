using HemodinksAPI.Application.Authentication;

namespace HemodinksAPI.Api;

public sealed class AuthenticationSessionMiddleware
{
    private readonly RequestDelegate _next;

    public AuthenticationSessionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        AuthenticationSessionService sessionService,
        AuthenticationSessionCookie sessionCookie)
    {
        if (context.Request.Path.StartsWithSegments("/api/session/renovar", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/session/sair", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var sessionIdClaim = context.User.FindFirst(AuthenticationSessionClaimTypes.SessionId)?.Value;
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(sessionIdClaim, out var sessionId)
            && !await sessionService.ValidateAndTouchAsync(sessionId, context.RequestAborted))
        {
            sessionCookie.Delete(context);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Sessao expirada por inatividade. Autentique-se novamente."
            }, context.RequestAborted);
            return;
        }

        await _next(context);
    }
}
