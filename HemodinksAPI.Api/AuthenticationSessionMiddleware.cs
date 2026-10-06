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
        SessionLifetimePolicy lifetime)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null
            || context.Request.Path.StartsWithSegments("/api/session/renovar", StringComparison.OrdinalIgnoreCase)
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
                await RejectSessionAsync(context, validation.FailureCode);
                return;
            }

            var membershipIdClaim = context.User.FindFirst(
                GlobalIdentityClaimTypes.UsuarioClinicaId)?.Value;
            if (!int.TryParse(membershipIdClaim, out var membershipId)
                || validation.UsuarioClinicaId != membershipId)
            {
                await RejectSessionAsync(context);
                return;
            }

            SynchronizeProfileClaims(context.User, validation);
            if (validation.Snapshot is { } snapshot) ValidatedSessionRequest.Set(context, snapshot);
            if (validation.AuthenticatedAt.HasValue && context.User.Identity is System.Security.Claims.ClaimsIdentity identity)
                ReplaceClaim(identity, AuthenticationSessionClaimTypes.AuthenticatedAt,
                    new DateTimeOffset(DateTime.SpecifyKind(validation.AuthenticatedAt.Value, DateTimeKind.Utc))
                        .ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (context.User.Identity?.IsAuthenticated == true)
        {
            var startedAt = SessionLifetimePolicy.ParseAuthenticationTime(
                context.User.FindFirst(AuthenticationSessionClaimTypes.AuthenticatedAt)?.Value);
            var failure = sessionIdClaim != null ? SessionLifetimePolicy.ReauthenticationRequired : lifetime.Failure(startedAt);
            if (failure != null)
            {
                await RejectSessionAsync(context, failure);
                return;
            }
        }

        await _next(context);
    }

    private static async Task RejectSessionAsync(HttpContext context, string? code = null)
    {
        // A late request may carry an old token after a login or clinic switch.
        // Only explicit, cookie-bound logout may remove the current refresh cookie.
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            code,
            message = code == SessionLifetimePolicy.AbsoluteExpired ? SessionLifetimePolicy.AbsoluteExpiredMessage
                : "Sessao expirada ou usuario inativo. Autentique-se novamente."
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
