using HemodinksAPI.Application.Features.Teams;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformTeamRequestHandler
{
        public async Task<PlatformUseCaseResult> ListClinicTeams(
        int id,
        CancellationToken cancellationToken)
        {
        if (!await context.Clinicas.AsNoTracking().AnyAsync(item => item.Id == id, cancellationToken))
        {
        return PlatformUseCaseResult.NotFound();
        }
        
        var teams = await context.Equipes
        .AsNoTracking()
        .Where(item => item.ClinicaId == id)
        .OrderBy(item => item.Nome)
        .Select(item => new TeamResponse(
        item.Id,
        item.Nome,
        item.UsuarioLoginId,
        item.UsuarioLogin.Email,
        item.ModoIdentificacao,
        item.Ativa,
        item.Membros.Where(member => member.Ativo).Select(member => new TeamMemberResponse(
        member.UserId,
        member.User.Nome,
        member.User.Email,
        member.User.PerfilId,
        item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.Id).FirstOrDefault(),
        item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.Ativo).FirstOrDefault(),
        item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.PinHash != null).FirstOrDefault(),
        item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.PrecisaTrocarPin).FirstOrDefault(),
        item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.BloqueadoAte).FirstOrDefault()
        )).ToList()))
        .ToListAsync(cancellationToken);
        
        return PlatformUseCaseResult.Ok(teams);
        }

        public async Task<PlatformUseCaseResult> ListClinicTeamUsers(
        int id,
        CancellationToken cancellationToken)
        {
        if (!await context.Clinicas.AsNoTracking().AnyAsync(item => item.Id == id, cancellationToken))
        {
        return PlatformUseCaseResult.NotFound();
        }
        
        var eligibleMemberships = await context.UsuariosClinicas
        .AsNoTracking()
        .Where(item => item.ClinicaId == id
        && item.Ativo
        && item.UsuarioGlobal.Ativo
        && item.User.Ativo
        && !context.Equipes.Any(team => team.UsuarioLoginId == item.UserId)
        && (item.PerfilId == Perfil.MedicosId
        || item.PerfilId == Perfil.ControllerId))
        .Select(item => new
        {
        item.UsuarioGlobalId,
        item.UsuarioGlobal.Nome,
        item.UsuarioGlobal.Email,
        item.PerfilId,
        PerfilNome = item.Perfil.Nome,
        OrigemClinica = item.Clinica.Nome
        })
        .ToListAsync(cancellationToken);
        
        var targetUsers = await context.UsuariosClinicas
        .AsNoTracking()
        .Where(item => item.ClinicaId == id)
        .Select(item => new { item.UsuarioGlobalId, item.UserId })
        .ToDictionaryAsync(item => item.UsuarioGlobalId, item => item.UserId, cancellationToken);
        
        var candidates = eligibleMemberships
        .GroupBy(item => item.UsuarioGlobalId)
        .Select(group => group
        .OrderByDescending(item => item.PerfilId == Perfil.MedicosId)
        .First())
        .Select(item => new ClinicTeamUserResponse(
        item.UsuarioGlobalId,
        targetUsers.GetValueOrDefault(item.UsuarioGlobalId) is var userId && userId != 0 ? userId : null,
        item.Nome,
        item.Email,
        item.PerfilId,
        item.PerfilNome,
        item.OrigemClinica,
        targetUsers.ContainsKey(item.UsuarioGlobalId)))
        .ToList();
        
        var localTeamUsers = await context.Users
        .AsNoTracking()
        .Where(item => item.ClinicaId == id
        && item.Ativo
        && item.PerfilId == Perfil.EquipeId
        && !context.UsuariosClinicas.Any(link => link.UserId == item.Id)
        && !context.Equipes.Any(team => team.UsuarioLoginId == item.Id))
        .Select(item => new ClinicTeamUserResponse(
        null,
        item.Id,
        item.Nome,
        item.Email,
        item.PerfilId,
        item.Perfil.Nome,
        item.Clinica.Nome,
        true))
        .ToListAsync(cancellationToken);
        
        candidates.AddRange(localTeamUsers);
        candidates = candidates.OrderBy(item => item.Nome).ToList();
        
        return PlatformUseCaseResult.Ok(candidates);
        }
}
