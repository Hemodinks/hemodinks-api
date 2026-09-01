using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Sessions;

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
            && Guid.TryParse(sessionIdClaim, out var sessionId))
        {
            var validation = await sessionService.ValidateAndTouchAsync(sessionId, context.RequestAborted);
            if (!validation.IsValid)
            {
                await RejectSessionAsync(context, sessionCookie);
                return;
            }

            var membershipIdClaim = context.User.FindFirst(
                GlobalIdentityClaimTypes.UsuarioClinicaId)?.Value;
            if (!int.TryParse(membershipIdClaim, out var membershipId)
                || validation.UsuarioClinicaId != membershipId)
            {
                await RejectSessionAsync(context, sessionCookie);
                return;
            }

            SynchronizeProfileClaims(context.User, validation);
        }

        await _next(context);
    }

    private static async Task RejectSessionAsync(
        HttpContext context,
        AuthenticationSessionCookie sessionCookie)
    {
        sessionCookie.Delete(context);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Sessao expirada ou usuario inativo. Autentique-se novamente."
        }, context.RequestAborted);
    }

    private static void SynchronizeProfileClaims(
        System.Security.Claims.ClaimsPrincipal principal,
        AuthenticationSessionValidation validation)
    {
        if (validation.PerfilId is not int perfilId
            || principal.Identity is not System.Security.Claims.ClaimsIdentity identity)
        {
            return;
        }

        ReplaceClaim(identity, "perfilId", perfilId.ToString());
        ReplaceClaim(identity, "perfilNome", validation.PerfilNome ?? string.Empty);
        ReplaceClaim(identity, System.Security.Claims.ClaimTypes.Role, validation.PerfilNome ?? string.Empty);
    }

    private static void ReplaceClaim(
        System.Security.Claims.ClaimsIdentity identity,
        string type,
        string value)
    {
        foreach (var claim in identity.FindAll(type).ToList())
        {
            identity.RemoveClaim(claim);
        }

        identity.AddClaim(new System.Security.Claims.Claim(type, value));
    }
}
