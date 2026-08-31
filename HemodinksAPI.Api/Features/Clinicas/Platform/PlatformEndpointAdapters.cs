using System.Security.Claims;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Clinics.Platform;
using HemodinksAPI.Application.Tenancy;

namespace HemodinksAPI.Api;

internal static class PlatformEndpointAdapters
{
    public static PlatformRequestContext ToPlatformRequestContext(this HttpContext context) => new(
        ReadInt(context.User, GlobalIdentityClaimTypes.UsuarioGlobalId),
        ReadInt(context.User, ClaimTypes.NameIdentifier),
        ReadInt(context.User, "perfilId"),
        ReadInt(context.User, ClinicaClaimTypes.ClinicaId),
        ReadInt(context.User, GlobalIdentityClaimTypes.EquipeId),
        ReadInt(context.User, GlobalIdentityClaimTypes.EquipeOperadorId),
        context.Connection.RemoteIpAddress?.ToString(),
        context.Request.Headers.UserAgent.ToString(),
        context.TraceIdentifier);

    public static async Task<IResult> ToHttpResultAsync(this Task<PlatformUseCaseResult> task)
    {
        var result = await task;
        return result.Kind switch
        {
            PlatformResultKind.Ok => Results.Ok(result.Value),
            PlatformResultKind.Created => Results.Created(result.Location!, result.Value),
            PlatformResultKind.NoContent => Results.NoContent(),
            PlatformResultKind.BadRequest => Results.BadRequest(result.Value),
            PlatformResultKind.Forbidden => Results.Forbid(),
            PlatformResultKind.NotFound => Results.NotFound(),
            PlatformResultKind.Conflict => Results.Conflict(result.Value),
            _ => throw new InvalidOperationException("Resultado de plataforma não suportado.")
        };
    }

    private static int? ReadInt(ClaimsPrincipal principal, string claimType) =>
        int.TryParse(principal.FindFirstValue(claimType), out var value) ? value : null;
}
