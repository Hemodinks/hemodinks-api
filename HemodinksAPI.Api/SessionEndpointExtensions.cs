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
        await EnsureSuperAdministratorMembershipsAsync(
            principal,
            context,
            requestedClinicId: null,
            cancellationToken);
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
        await EnsureSuperAdministratorMembershipsAsync(
            httpContext.User,
            context,
            request.ClinicaId,
            cancellationToken);
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

    private static async Task EnsureSuperAdministratorMembershipsAsync(
        ClaimsPrincipal principal,
        AppDbContext context,
        int? requestedClinicId,
        CancellationToken cancellationToken)
    {
        if (principal.FindFirstValue("perfilId") != Perfil.SuperAdministradorId.ToString()
            || !TryGetGlobalUserId(principal, out var globalUserId)
            || !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var sourceUserId))
        {
            return;
        }

        var source = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == sourceUserId
                && item.PerfilId == Perfil.SuperAdministradorId
                && item.Ativo,
                cancellationToken);
        if (source == null)
        {
            return;
        }

        var clinicIdsQuery = context.Clinicas
            .AsNoTracking()
            .Where(item => item.Ativa);
        if (requestedClinicId.HasValue)
        {
            clinicIdsQuery = clinicIdsQuery.Where(item => item.Id == requestedClinicId.Value);
        }

        var clinicIds = await clinicIdsQuery
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var memberships = await context.UsuariosClinicas
            .Include(item => item.User)
            .Where(item => item.UsuarioGlobalId == globalUserId && clinicIds.Contains(item.ClinicaId))
            .ToDictionaryAsync(item => item.ClinicaId, cancellationToken);

        foreach (var clinicId in clinicIds)
        {
            if (memberships.TryGetValue(clinicId, out var existingMembership))
            {
                existingMembership.Ativo = true;
                existingMembership.PerfilId = Perfil.SuperAdministradorId;
                existingMembership.DataAtualizacao = DateTime.UtcNow;
                existingMembership.User.Ativo = true;
                existingMembership.User.PerfilId = Perfil.SuperAdministradorId;
                existingMembership.User.DataAtualizacao = DateTime.UtcNow;
                continue;
            }

            var localUser = await context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.ClinicaId == clinicId && item.Email == source.Email, cancellationToken);
            if (localUser == null)
            {
                localUser = new User
                {
                    ClinicaId = clinicId,
                    Nome = source.Nome,
                    Email = source.Email,
                    Telefone = $"+559{clinicId:00000000000}",
                    Senha = source.Senha,
                    DataNascimento = source.DataNascimento,
                    DataCadastro = DateTime.UtcNow,
                    Ativo = true,
                    PrecisaTrocarSenha = source.PrecisaTrocarSenha,
                    PerfilId = Perfil.SuperAdministradorId
                };
                context.Users.Add(localUser);
            }
            else
            {
                localUser.Ativo = true;
                localUser.PerfilId = Perfil.SuperAdministradorId;
                localUser.DataAtualizacao = DateTime.UtcNow;
            }

            await context.SaveChangesAsync(cancellationToken);
            await GlobalIdentityService.EnsureForUserAsync(context, localUser, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
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
