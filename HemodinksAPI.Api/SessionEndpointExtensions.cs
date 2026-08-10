using System.Security.Claims;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
        AppDbContext context,
        ClinicaContext clinicaContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetGlobalUserId(principal, out var globalUserId))
        {
            return Results.Unauthorized();
        }

        clinicaContext.SetPlatformScope();
        var memberships = await context.UsuariosClinicas
            .AsNoTracking()
            .Where(item => item.UsuarioGlobalId == globalUserId
                && item.Ativo
                && item.UsuarioGlobal.Ativo
                && item.User.Ativo
                && item.Clinica.Ativa)
            .OrderByDescending(item => item.ClinicaPadrao)
            .ThenBy(item => item.Clinica.Nome)
            .Select(item => new
            {
                item.ClinicaId,
                item.Clinica.Nome,
                item.Clinica.Slug,
                item.UserId,
                item.PerfilId,
                Perfil = item.Perfil.Nome,
                item.Clinica.Plano,
                item.Clinica.ModulosLiberados,
                item.ClinicaPadrao,
                UsuarioClinicaId = item.Id
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(memberships.Select(item => new SessionClinicResponse(
            item.ClinicaId,
            item.Nome,
            item.Slug,
            item.UserId,
            item.PerfilId,
            item.Perfil,
            ClinicaModulos.GetEffective(item.Plano, item.ModulosLiberados),
            item.ClinicaPadrao,
            item.UsuarioClinicaId)));
    }

    private static async Task<IResult> SelectClinica(
        SelectClinicRequest request,
        HttpContext httpContext,
        AppDbContext context,
        ClinicaContext clinicaContext,
        IJwtTokenService jwtTokenService,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        if (request.ClinicaId <= 0 || !TryGetGlobalUserId(httpContext.User, out var globalUserId))
        {
            return Results.BadRequest(new { message = "ClinicaId invalido ou identidade global ausente." });
        }

        clinicaContext.SetPlatformScope();
        var membership = await context.UsuariosClinicas
            .Include(item => item.UsuarioGlobal)
            .Include(item => item.Clinica)
            .Include(item => item.Perfil)
            .Include(item => item.User).ThenInclude(item => item.Perfil)
            .Include(item => item.User).ThenInclude(item => item.Clinica)
            .FirstOrDefaultAsync(item => item.UsuarioGlobalId == globalUserId
                && item.ClinicaId == request.ClinicaId
                && item.Ativo
                && item.UsuarioGlobal.Ativo
                && item.User.Ativo
                && item.Clinica.Ativa,
                cancellationToken);

        if (membership == null)
        {
            await auditService.RecordAsync(
                httpContext,
                "session.clinic.switch.denied",
                "session",
                request.ClinicaId.ToString(),
                null,
                new { requestedClinicId = request.ClinicaId },
                false,
                cancellationToken);
            return Results.Forbid();
        }

        var previousClinicId = int.TryParse(
            httpContext.User.FindFirstValue(ClinicaClaimTypes.ClinicaId),
            out var parsedClinicId)
            ? parsedClinicId
            : (int?)null;
        var token = jwtTokenService.GenerateToken(membership.UsuarioGlobal, membership, membership.User);

        await auditService.RecordAsync(
            httpContext,
            "session.clinic.switch",
            "session",
            membership.Id.ToString(),
            membership.ClinicaId,
            new { previousClinicId, selectedClinicId = membership.ClinicaId },
            true,
            cancellationToken);

        return Results.Ok(new SelectClinicResponse(
            token,
            membership.UsuarioGlobalId,
            new SessionClinicResponse(
                membership.ClinicaId,
                membership.Clinica.Nome,
                membership.Clinica.Slug,
                membership.UserId,
                membership.PerfilId,
                membership.Perfil.Nome,
                ClinicaModulos.GetEffective(membership.Clinica.Plano, membership.Clinica.ModulosLiberados),
                membership.ClinicaPadrao,
                membership.Id)));
    }

    private static bool TryGetGlobalUserId(ClaimsPrincipal principal, out int globalUserId)
    {
        return int.TryParse(
            principal.FindFirstValue(GlobalIdentityClaimTypes.UsuarioGlobalId),
            out globalUserId)
            && globalUserId > 0;
    }

    public sealed record SelectClinicRequest(int ClinicaId);

    public sealed record SessionClinicResponse(
        int ClinicaId,
        string Nome,
        string Slug,
        int UserId,
        int PerfilId,
        string Perfil,
        IReadOnlyList<string> ModulosLiberados,
        bool ClinicaPadrao,
        int UsuarioClinicaId);

    public sealed record SelectClinicResponse(
        string Token,
        int UsuarioGlobalId,
        SessionClinicResponse Clinica);
}
