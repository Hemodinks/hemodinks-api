using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Teams;

public sealed partial class TeamUseCases
{
    public async Task<TeamUseCaseResult<AssociateTeamMemberResponse>> AssociateMemberAsync(
        int teamId, int userId, bool generatePin, CancellationToken cancellationToken)
    {
        var team = await context.Equipes.FirstOrDefaultAsync(item => item.Id == teamId && item.Ativa, cancellationToken);
        var user = await context.Users.FirstOrDefaultAsync(item => item.Id == userId && item.Ativo
            && (item.PerfilId == Perfil.MedicosId || item.PerfilId == Perfil.ControllerId), cancellationToken);
        if (team == null || user == null) return TeamUseCaseResult<AssociateTeamMemberResponse>.NotFound();
        if (generatePin && !team.ModoIdentificacao.Equals(EquipeModosIdentificacao.Pin, StringComparison.OrdinalIgnoreCase))
            return TeamUseCaseResult<AssociateTeamMemberResponse>.BadRequest("Ative o modo de identificacao por PIN antes de gerar um PIN individual");

        var member = await context.EquipeMembros.FirstOrDefaultAsync(item => item.EquipeId == teamId && item.UserId == user.Id, cancellationToken);
        if (member == null)
        {
            member = new EquipeMembro { ClinicaId = team.ClinicaId, EquipeId = teamId, UserId = user.Id };
            context.EquipeMembros.Add(member);
        }
        else
        {
            member.Ativo = true;
            member.DataAtualizacao = DateTime.UtcNow;
        }

        var op = await context.EquipeOperadores.FirstOrDefaultAsync(item => item.EquipeId == teamId && item.UserId == user.Id, cancellationToken);
        if (op == null)
        {
            op = new EquipeOperador { ClinicaId = team.ClinicaId, EquipeId = teamId, UserId = user.Id };
            context.EquipeOperadores.Add(op);
        }
        op.Ativo = true;
        op.VersaoSessao++;

        string? temporaryPin = null;
        if (generatePin)
        {
            temporaryPin = EquipeAuthenticationRules.GeneratePin();
            op.PinHash = passwordHasher.HashPassword(temporaryPin);
            op.PrecisaTrocarPin = true;
            op.DataUltimaTroca = DateTime.UtcNow;
        }
        else
        {
            op.PinHash = null;
            op.PrecisaTrocarPin = false;
            op.TentativasFalhas = 0;
            op.BloqueadoAte = null;
        }

        team.VersaoSessao++;
        await context.SaveChangesAsync(cancellationToken);
        return TeamUseCaseResult<AssociateTeamMemberResponse>.Success(new AssociateTeamMemberResponse(op.Id, temporaryPin),
            TeamAudit.Create("team.member.add", "team-member", user.Id, team.ClinicaId,
                new Dictionary<string, object?> { ["equipeId"] = team.Id, ["userId"] = user.Id, ["pinGenerated"] = temporaryPin != null }));
    }

    public async Task<TeamUseCaseResult> RemoveMemberAsync(int teamId, int userId, CancellationToken cancellationToken)
    {
        var member = await context.EquipeMembros.FirstOrDefaultAsync(item => item.EquipeId == teamId && item.UserId == userId, cancellationToken);
        if (member == null) return TeamUseCaseResult.NotFound();
        member.Ativo = false;
        member.DataAtualizacao = DateTime.UtcNow;
        var op = await context.EquipeOperadores.FirstOrDefaultAsync(item => item.EquipeId == teamId && item.UserId == userId, cancellationToken);
        if (op != null)
        {
            op.Ativo = false;
            op.VersaoSessao++;
            op.DataAtualizacao = DateTime.UtcNow;
        }
        var team = await context.Equipes.FirstAsync(item => item.Id == teamId, cancellationToken);
        team.VersaoSessao++;
        await context.SaveChangesAsync(cancellationToken);
        return TeamUseCaseResult.Success(TeamAudit.Create("team.member.remove", "team-member", userId, team.ClinicaId,
            new Dictionary<string, object?> { ["equipeId"] = teamId, ["userId"] = userId }));
    }

    public async Task<TeamUseCaseResult<string>> ResetPinAsync(int teamId, int operatorId, CancellationToken cancellationToken)
    {
        var team = await context.Equipes.AsNoTracking().FirstOrDefaultAsync(item => item.Id == teamId && item.Ativa, cancellationToken);
        if (team == null || !team.ModoIdentificacao.Equals(EquipeModosIdentificacao.Pin, StringComparison.OrdinalIgnoreCase))
            return TeamUseCaseResult<string>.BadRequest("A equipe nao utiliza identificacao por PIN");
        var op = await context.EquipeOperadores.FirstOrDefaultAsync(item => item.Id == operatorId && item.EquipeId == teamId && item.Ativo, cancellationToken);
        if (op == null) return TeamUseCaseResult<string>.NotFound();
        var pin = EquipeAuthenticationRules.GeneratePin();
        op.PinHash = passwordHasher.HashPassword(pin);
        op.PrecisaTrocarPin = true;
        op.TentativasFalhas = 0;
        op.BloqueadoAte = null;
        op.VersaoSessao++;
        op.DataUltimaTroca = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return TeamUseCaseResult<string>.Success(pin, TeamAudit.Create("team.operator.pin.reset", "team-operator", op.Id, op.ClinicaId,
            new Dictionary<string, object?> { ["equipeId"] = teamId, ["operadorId"] = operatorId }));
    }

    public async Task<TeamUseCaseResult> SetOperatorBlockedAsync(int teamId, int operatorId, bool blocked, CancellationToken cancellationToken)
    {
        var op = await context.EquipeOperadores.FirstOrDefaultAsync(item => item.Id == operatorId && item.EquipeId == teamId, cancellationToken);
        if (op == null) return TeamUseCaseResult.NotFound();
        op.Ativo = !blocked;
        op.BloqueadoAte = blocked ? DateTime.MaxValue : null;
        op.TentativasFalhas = 0;
        op.VersaoSessao++;
        await context.SaveChangesAsync(cancellationToken);
        return TeamUseCaseResult.Success();
    }

}
