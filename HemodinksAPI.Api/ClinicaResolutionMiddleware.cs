using HemodinksAPI.Application.Tenancy;
using System.Security.Claims;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public sealed class ClinicaResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public ClinicaResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext httpContext,
        ClinicaContext clinicaContext,
        ClinicaResolutionService clinicaResolutionService,
        AppDbContext dbContext,
        ILogger<ClinicaResolutionMiddleware> logger)
    {
        if (!ShouldResolveClinica(httpContext.Request.Path))
        {
            await _next(httpContext);
            return;
        }

        var resolvedClinica = await clinicaResolutionService.ResolveAsync(httpContext, httpContext.RequestAborted);
        if (resolvedClinica == null)
        {
            logger.LogWarning(
                "Clinica nao resolvida para {Method} {Path}. Header {HeaderName} ausente/invalido e nenhum fallback aplicavel.",
                httpContext.Request.Method,
                httpContext.Request.Path,
                ClinicaResolutionService.ClinicaSlugHeaderName);

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                message = "Clinica nao resolvida. Envie X-Clinica-Slug ou use um subdominio configurado."
            }, httpContext.RequestAborted);
            return;
        }

        clinicaContext.SetCurrent(resolvedClinica.Id, resolvedClinica.Slug);
        if (!await ApplyEffectiveClinicClaimsAsync(httpContext.User, resolvedClinica, dbContext, httpContext.RequestAborted))
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                message = "Superadministrador sem identidade local na clinica selecionada. Execute o provisionamento da plataforma."
            }, httpContext.RequestAborted);
            return;
        }
        httpContext.Response.Headers["X-Clinica-Slug"] = resolvedClinica.Slug;

        await _next(httpContext);
    }

    private static bool ShouldResolveClinica(PathString path)
    {
        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments("/api/platform", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ApplyEffectiveClinicClaimsAsync(
        ClaimsPrincipal principal,
        ResolvedClinica clinica,
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        if (!principal.HasClaim("perfilId", Perfil.SuperAdministradorId.ToString())
            || principal.Identity is not ClaimsIdentity identity)
        {
            return true;
        }

        var email = principal.FindFirstValue(ClaimTypes.Email);
        var localIdentity = await context.Users
            .AsNoTracking()
            .Where(item => item.Email == email
                && item.PerfilId == Perfil.SuperAdministradorId
                && item.Ativo)
            .Select(item => new { item.Id, item.Nome })
            .FirstOrDefaultAsync(cancellationToken);

        if (localIdentity == null)
        {
            return false;
        }

        var originalId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!identity.HasClaim(claim => claim.Type == "platformActorId") && originalId != null)
        {
            identity.AddClaim(new Claim("platformActorId", originalId));
        }

        ReplaceClaims(identity, ClaimTypes.NameIdentifier, localIdentity.Id.ToString());
        ReplaceClaims(identity, ClaimTypes.Name, localIdentity.Nome);
        ReplaceClaims(identity, ClinicaClaimTypes.ClinicaId, clinica.Id.ToString());
        ReplaceClaims(identity, ClinicaClaimTypes.ClinicaSlug, clinica.Slug);

        return true;
    }

    private static void ReplaceClaims(ClaimsIdentity identity, string claimType, string value)
    {
        foreach (var claim in identity.FindAll(claimType).ToList())
        {
            identity.TryRemoveClaim(claim);
        }

        identity.AddClaim(new Claim(claimType, value));
    }
}
