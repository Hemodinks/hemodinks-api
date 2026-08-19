using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Sessions;

public sealed class SessionUseCases(
    ISessionDbContext context,
    IClinicaContext clinicaContext,
    IJwtTokenService jwtTokenService)
{
    public async Task<IReadOnlyList<SessionClinicResponse>> ListClinicsAsync(
        CurrentUserContext currentUser,
        CancellationToken cancellationToken)
    {
        clinicaContext.SetPlatformScope();
        await EnsureSuperAdministratorMembershipsAsync(currentUser, null, cancellationToken);

        var memberships = await context.UsuariosClinicas
            .AsNoTracking()
            .Where(item => item.UsuarioGlobalId == currentUser.UsuarioGlobalId
                && item.Ativo && item.UsuarioGlobal.Ativo && item.User.Ativo && item.Clinica.Ativa)
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

        return memberships.Select(item => new SessionClinicResponse(
            item.ClinicaId, item.Nome, item.Slug, item.UserId, item.PerfilId, item.Perfil,
            ClinicaModulos.GetEffective(item.Plano, item.ModulosLiberados),
            item.ClinicaPadrao, item.UsuarioClinicaId)).ToList();
    }

    public async Task<SelectClinicResponse?> SelectClinicAsync(
        int clinicId,
        CurrentUserContext currentUser,
        Guid? sessionId,
        CancellationToken cancellationToken)
    {
        clinicaContext.SetPlatformScope();
        await EnsureSuperAdministratorMembershipsAsync(currentUser, clinicId, cancellationToken);

        var membership = await context.UsuariosClinicas
            .Include(item => item.UsuarioGlobal)
            .Include(item => item.Clinica)
            .Include(item => item.Perfil)
            .Include(item => item.User).ThenInclude(item => item.Perfil)
            .Include(item => item.User).ThenInclude(item => item.Clinica)
            .FirstOrDefaultAsync(item => item.UsuarioGlobalId == currentUser.UsuarioGlobalId
                && item.ClinicaId == clinicId && item.Ativo && item.UsuarioGlobal.Ativo
                && item.User.Ativo && item.Clinica.Ativa, cancellationToken);

        if (membership == null) return null;

        var token = jwtTokenService.GenerateToken(
            membership.UsuarioGlobal,
            membership,
            membership.User,
            sessionId);
        return new SelectClinicResponse(token, membership.UsuarioGlobalId, new SessionClinicResponse(
            membership.ClinicaId, membership.Clinica.Nome, membership.Clinica.Slug,
            membership.UserId, membership.PerfilId, membership.Perfil.Nome,
            ClinicaModulos.GetEffective(membership.Clinica.Plano, membership.Clinica.ModulosLiberados),
            membership.ClinicaPadrao, membership.Id));
    }

    private async Task EnsureSuperAdministratorMembershipsAsync(
        CurrentUserContext currentUser,
        int? requestedClinicId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsSuperAdministrador || currentUser.UsuarioGlobalId <= 0) return;

        var source = await context.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == currentUser.Id
                && item.PerfilId == Perfil.SuperAdministradorId && item.Ativo, cancellationToken);
        if (source == null) return;

        var clinicIdsQuery = context.Clinicas.AsNoTracking().Where(item => item.Ativa);
        if (requestedClinicId.HasValue)
        {
            clinicIdsQuery = clinicIdsQuery.Where(item => item.Id == requestedClinicId.Value);
        }

        var clinicIds = await clinicIdsQuery.Select(item => item.Id).ToListAsync(cancellationToken);
        var memberships = await context.UsuariosClinicas
            .Include(item => item.User)
            .Where(item => item.UsuarioGlobalId == currentUser.UsuarioGlobalId && clinicIds.Contains(item.ClinicaId))
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

            var localUser = await context.Users.IgnoreQueryFilters()
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
}

public sealed record SessionClinicResponse(int ClinicaId, string Nome, string Slug, int UserId, int PerfilId,
    string Perfil, IReadOnlyList<string> ModulosLiberados, bool ClinicaPadrao, int UsuarioClinicaId);

public sealed record SelectClinicResponse(string Token, int UsuarioGlobalId, SessionClinicResponse Clinica);
