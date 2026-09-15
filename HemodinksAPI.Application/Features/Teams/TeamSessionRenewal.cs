using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Teams;

public sealed partial class TeamUseCases
{
    public async Task<string?> RenewSessionAsync(CurrentUserContext user, int teamVersion,
        int? operatorVersion, Guid securityVersion, CancellationToken cancellationToken)
    {
        if (!user.IsEquipe || !user.EquipeId.HasValue) return null;
        var membership = await context.UsuariosClinicas.Include(x => x.UsuarioGlobal)
            .Include(x => x.User).ThenInclude(x => x.Perfil)
            .Include(x => x.User).ThenInclude(x => x.Clinica)
            .FirstOrDefaultAsync(x => x.Id == user.UsuarioClinicaId && x.UserId == user.Id
                && x.UsuarioGlobalId == user.UsuarioGlobalId && x.ClinicaId == user.ClinicaId
                && x.Ativo && x.UsuarioGlobal.Ativo && x.User.Ativo && x.Clinica.Ativa
                && x.User.PerfilId == Perfil.EquipeId && x.PerfilId == Perfil.EquipeId
                && x.UsuarioGlobal.SecurityVersion == securityVersion, cancellationToken);
        if (membership == null) return null;
        var team = await context.Equipes.FirstOrDefaultAsync(x => x.Id == user.EquipeId
            && x.ClinicaId == user.ClinicaId && x.UsuarioLoginId == user.Id && x.Ativa
            && x.VersaoSessao == teamVersion, cancellationToken);
        if (team == null) return null;
        EquipeOperador? op = null;
        if (user.EquipeOperadorId.HasValue)
        {
            op = await context.EquipeOperadores.FirstOrDefaultAsync(x => x.Id == user.EquipeOperadorId
                && x.EquipeId == team.Id && x.ClinicaId == user.ClinicaId && x.Ativo
                && x.User.Ativo && x.User.ClinicaId == user.ClinicaId
                && x.VersaoSessao == operatorVersion
                && context.EquipeMembros.Any(m => m.EquipeId == team.Id && m.ClinicaId == user.ClinicaId
                    && m.UserId == x.UserId && m.Ativo), cancellationToken);
            if (op == null) return null;
        }
        else if (!team.ModoIdentificacao.Equals(EquipeModosIdentificacao.Nenhuma, StringComparison.OrdinalIgnoreCase))
            return null;
        return jwtTokenService.GenerateToken(membership.UsuarioGlobal, membership, membership.User,
            team, op, user.IdentificacaoConfiavel);
    }
}
