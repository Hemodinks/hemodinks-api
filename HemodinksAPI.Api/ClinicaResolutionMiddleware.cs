using HemodinksAPI.Application.Tenancy;
using System.Security.Claims;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
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
        IPlatformTeamDbContext dbContext,
        ILogger<ClinicaResolutionMiddleware> logger)
    {
        if (IsPasswordResetConfirmation(httpContext.Request))
        {
            // O token e a credencial deste endpoint e identifica a clinica dona do
            // reset. O endpoint resolve esse tenant antes de acessar os dados.
            await _next(httpContext);
            return;
        }

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

        if (httpContext.User.Identity?.IsAuthenticated == true
            && string.Equals(httpContext.User.FindFirstValue("precisaTrocarPin"), "true", StringComparison.OrdinalIgnoreCase)
            && !httpContext.Request.Path.StartsWithSegments("/api/equipe-auth/pin", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                message = "Troque o PIN temporario antes de continuar."
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

    private static bool IsPasswordResetConfirmation(HttpRequest request)
    {
        return HttpMethods.IsPost(request.Method)
            && request.Path.Equals("/api/users/password/reset/confirm", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ValidateActiveMembershipAsync(
        ClaimsPrincipal principal,
        ResolvedClinica clinica,
        IPlatformTeamDbContext context,
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

        var hasActiveMembership = await context.UsuariosClinicas
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

        if (!hasActiveMembership || principal.FindFirstValue("perfilId") != Perfil.EquipeId.ToString())
        {
            return hasActiveMembership;
        }

        if (!int.TryParse(principal.FindFirstValue(GlobalIdentityClaimTypes.EquipeId), out var equipeId)
            || !int.TryParse(principal.FindFirstValue(GlobalIdentityClaimTypes.EquipeVersaoSessao), out var equipeVersao))
        {
            return false;
        }

        var equipe = await context.Equipes
            .AsNoTracking()
            .Where(item => item.Id == equipeId
                && item.ClinicaId == clinica.Id
                && item.UsuarioLoginId == userId
                && item.Ativa
                && item.VersaoSessao == equipeVersao)
            .Select(item => new { item.Id, item.ModoIdentificacao })
            .FirstOrDefaultAsync(cancellationToken);
        if (equipe == null)
        {
            return false;
        }

        var operadorClaim = principal.FindFirstValue(GlobalIdentityClaimTypes.EquipeOperadorId);
        if (!int.TryParse(operadorClaim, out var operadorId))
        {
            return equipe.ModoIdentificacao.Equals(EquipeModosIdentificacao.Nenhuma, StringComparison.OrdinalIgnoreCase);
        }

        return int.TryParse(principal.FindFirstValue(GlobalIdentityClaimTypes.OperadorVersaoSessao), out var operadorVersao)
            && await context.EquipeOperadores
                .AsNoTracking()
                .AnyAsync(item => item.Id == operadorId
                    && item.EquipeId == equipe.Id
                    && item.ClinicaId == clinica.Id
                    && item.Ativo
                    && item.User.Ativo
                    && item.VersaoSessao == operadorVersao
                    && context.EquipeMembros.Any(membro => membro.EquipeId == equipe.Id
                        && membro.UserId == item.UserId
                        && membro.Ativo),
                    cancellationToken);
    }
}
