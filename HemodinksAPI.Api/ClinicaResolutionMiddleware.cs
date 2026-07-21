using HemodinksAPI.Application.Tenancy;
using System.Security.Claims;
using HemodinksAPI.Application.Authentication;
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
        if (!await ValidateActiveMembershipAsync(httpContext.User, resolvedClinica, dbContext, httpContext.RequestAborted))
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                message = "A sessao nao possui associacao ativa com a clinica selecionada. Selecione a clinica novamente."
            }, httpContext.RequestAborted);
            return;
        }
        httpContext.Response.Headers["X-Clinica-Slug"] = resolvedClinica.Slug;

        await _next(httpContext);
    }

    private static bool ShouldResolveClinica(PathString path)
    {
        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments("/api/public/clinicas", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ValidateActiveMembershipAsync(
        ClaimsPrincipal principal,
        ResolvedClinica clinica,
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var membershipIdValue = principal.FindFirstValue(GlobalIdentityClaimTypes.UsuarioClinicaId);
        var globalUserIdValue = principal.FindFirstValue(GlobalIdentityClaimTypes.UsuarioGlobalId);
        if (!int.TryParse(userIdValue, out var userId)
            || !int.TryParse(membershipIdValue, out var membershipId)
            || !int.TryParse(globalUserIdValue, out var globalUserId))
        {
            return false;
        }

        return await context.UsuariosClinicas
            .AsNoTracking()
            .AnyAsync(item => item.Id == membershipId
                && item.UsuarioGlobalId == globalUserId
                && item.UserId == userId
                && item.ClinicaId == clinica.Id
                && item.Ativo
                && item.UsuarioGlobal.Ativo
                && item.User.Ativo
                && item.Clinica.Ativa,
                cancellationToken);
    }
}
