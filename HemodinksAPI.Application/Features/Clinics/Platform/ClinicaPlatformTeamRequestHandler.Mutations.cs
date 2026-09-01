using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Teams;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformTeamRequestHandler
{
        public async Task<PlatformUseCaseResult> UpdateClinicTeam(
        int id,
        int teamId,
        AtualizarEquipeRequest request,
        PlatformRequestContext requestContext,
        CancellationToken cancellationToken)
        {
        var team = await context.Equipes
        .FirstOrDefaultAsync(item => item.Id == teamId && item.ClinicaId == id, cancellationToken);
        if (team == null) return PlatformUseCaseResult.NotFound();
        
        if (request.Nome != null) team.Nome = RequireText(request.Nome, "Nome da equipe invalido", 120);
        if (request.ModoIdentificacao != null) team.ModoIdentificacao = EquipeAuthenticationRules.NormalizeModo(request.ModoIdentificacao);
        if (request.Ativa.HasValue) team.Ativa = request.Ativa.Value;
        team.VersaoSessao++;
        team.DataAtualizacao = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(requestContext, "team.update", "team", team.Id.ToString(), id,
        new { team.Nome, team.ModoIdentificacao, team.Ativa }, true, cancellationToken);
        return PlatformUseCaseResult.NoContent();
        }

        public async Task<PlatformUseCaseResult> RemoveClinicTeamMember(
        int id,
        int teamId,
        int userId,
        PlatformRequestContext requestContext,
        CancellationToken cancellationToken)
        {
        var team = await context.Equipes
        .FirstOrDefaultAsync(item => item.Id == teamId && item.ClinicaId == id, cancellationToken);
        var member = await context.EquipeMembros
        .FirstOrDefaultAsync(item => item.ClinicaId == id && item.EquipeId == teamId && item.UserId == userId, cancellationToken);
        if (team == null || member == null) return PlatformUseCaseResult.NotFound();
        
        member.Ativo = false;
        member.DataAtualizacao = DateTime.UtcNow;
        var op = await context.EquipeOperadores
        .FirstOrDefaultAsync(item => item.ClinicaId == id && item.EquipeId == teamId && item.UserId == userId, cancellationToken);
        if (op != null)
        {
        op.Ativo = false;
        op.VersaoSessao++;
        op.DataAtualizacao = DateTime.UtcNow;
        }
        team.VersaoSessao++;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(requestContext, "team.member.remove", "team-member", userId.ToString(), id,
        new { equipeId = teamId, userId }, true, cancellationToken);
        return PlatformUseCaseResult.NoContent();
        }

        public async Task<PlatformUseCaseResult> ResetClinicTeamOperatorPin(
        int id,
        int teamId,
        int operatorId,
        PlatformRequestContext requestContext,
        CancellationToken cancellationToken)
        {
        var team = await context.Equipes.AsNoTracking()
        .FirstOrDefaultAsync(item => item.Id == teamId && item.ClinicaId == id && item.Ativa, cancellationToken);
        if (team == null) return PlatformUseCaseResult.NotFound();
        if (!team.ModoIdentificacao.Equals(EquipeModosIdentificacao.Pin, StringComparison.OrdinalIgnoreCase))
        {
        return PlatformUseCaseResult.BadRequest(new { message = "A equipe nao utiliza identificacao por PIN" });
        }
        
        var op = await context.EquipeOperadores
        .FirstOrDefaultAsync(item => item.Id == operatorId && item.EquipeId == teamId && item.ClinicaId == id && item.Ativo, cancellationToken);
        if (op == null) return PlatformUseCaseResult.NotFound();
        var pin = EquipeAuthenticationRules.GeneratePin();
        op.PinHash = passwordHasher.HashPassword(pin);
        op.PrecisaTrocarPin = true;
        op.TentativasFalhas = 0;
        op.BloqueadoAte = null;
        op.VersaoSessao++;
        op.DataUltimaTroca = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(requestContext, "team.operator.pin.reset", "team-operator", op.Id.ToString(), id,
        new { equipeId = teamId, operadorId = operatorId }, true, cancellationToken);
        return PlatformUseCaseResult.Ok(new { PinTemporario = pin });
        }
}
