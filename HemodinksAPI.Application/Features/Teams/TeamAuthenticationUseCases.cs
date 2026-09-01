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
    public async Task<TeamUseCaseResult<AuthenticateUserResponse>> IdentifyOperatorAsync(
        string challengeToken, int operatorId, string? pin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(challengeToken) || operatorId <= 0)
            return TeamUseCaseResult<AuthenticateUserResponse>.BadRequest("Token e operador sao obrigatorios");
        var tokenHash = EquipeAuthenticationRules.HashChallengeToken(challengeToken);
        var challenge = await context.EquipeLoginDesafios
            .Include(item => item.Equipe).ThenInclude(item => item.UsuarioLogin).ThenInclude(item => item.Perfil)
            .Include(item => item.Equipe).ThenInclude(item => item.UsuarioLogin).ThenInclude(item => item.Clinica)
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash && item.UtilizadoEm == null && item.ExpiraEm > DateTime.UtcNow, cancellationToken);
        if (challenge == null || !challenge.Equipe.Ativa) return TeamUseCaseResult<AuthenticateUserResponse>.Unauthorized();

        var op = await context.EquipeOperadores.Include(item => item.User)
            .FirstOrDefaultAsync(item => item.Id == operatorId && item.EquipeId == challenge.EquipeId && item.Ativo, cancellationToken);
        if (op == null || op.BloqueadoAte > DateTime.UtcNow
            || !await context.EquipeMembros.AnyAsync(item => item.EquipeId == challenge.EquipeId && item.UserId == op.UserId && item.Ativo, cancellationToken))
            return TeamUseCaseResult<AuthenticateUserResponse>.Unauthorized();

        var requiresPin = challenge.Equipe.ModoIdentificacao.Equals(EquipeModosIdentificacao.Pin, StringComparison.OrdinalIgnoreCase) && op.PinHash != null;
        if (requiresPin && (op.PinHash == null || !passwordHasher.VerifyPassword(pin ?? string.Empty, op.PinHash)))
        {
            op.TentativasFalhas++;
            if (op.TentativasFalhas >= 5)
            {
                op.BloqueadoAte = DateTime.UtcNow.AddMinutes(15);
                op.TentativasFalhas = 0;
                op.VersaoSessao++;
            }
            await context.SaveChangesAsync(cancellationToken);
            return TeamUseCaseResult<AuthenticateUserResponse>.Unauthorized();
        }

        op.TentativasFalhas = 0;
        op.BloqueadoAte = null;
        challenge.UtilizadoEm = DateTime.UtcNow;
        var membership = await context.UsuariosClinicas.Include(item => item.UsuarioGlobal)
            .FirstAsync(item => item.UserId == challenge.Equipe.UsuarioLoginId && item.Ativo, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        var loginUser = challenge.Equipe.UsuarioLogin;
        var jwt = jwtTokenService.GenerateToken(membership.UsuarioGlobal, membership, loginUser, challenge.Equipe, op, requiresPin);
        var license = await licencaService.GetCurrentAsync(new CurrentUserContext(loginUser.Id, loginUser.PerfilId,
            op.User.Nome, loginUser.ClinicaId, loginUser.Clinica.Slug, membership.UsuarioGlobalId, membership.Id,
            challenge.EquipeId, op.Id, requiresPin), cancellationToken);
        return TeamUseCaseResult<AuthenticateUserResponse>.Success(new AuthenticateUserResponse
        {
            Id = loginUser.Id,
            UsuarioGlobalId = membership.UsuarioGlobalId,
            ClinicaId = loginUser.ClinicaId,
            ClinicaSlug = loginUser.Clinica.Slug,
            Nome = op.User.Nome,
            Email = membership.UsuarioGlobal.Email,
            Token = jwt,
            PrecisaTrocarSenha = loginUser.PrecisaTrocarSenha,
            PrecisaTrocarPin = requiresPin && op.PrecisaTrocarPin,
            PerfilId = Perfil.EquipeId,
            PerfilNome = "Equipe",
            ModulosLiberados = ClinicaModulos.GetEffective(loginUser.Clinica.Plano, loginUser.Clinica.ModulosLiberados),
            Licenca = license
        });
    }

    public async Task<TeamUseCaseResult<ChangeTeamPinResponse>> ChangePinAsync(
        CurrentUserContext currentUser, string currentPin, string newPin, CancellationToken cancellationToken)
    {
        if (!currentUser.IsEquipe || !currentUser.EquipeId.HasValue || !currentUser.EquipeOperadorId.HasValue)
            return TeamUseCaseResult<ChangeTeamPinResponse>.Forbidden();
        if (!EquipeAuthenticationRules.IsValidPinFormat(currentPin) || !EquipeAuthenticationRules.IsValidPinFormat(newPin))
            return TeamUseCaseResult<ChangeTeamPinResponse>.BadRequest("O PIN deve possuir exatamente 6 numeros");
        if (currentPin == newPin)
            return TeamUseCaseResult<ChangeTeamPinResponse>.BadRequest("O novo PIN deve ser diferente do PIN temporario");

        var team = await context.Equipes.Include(item => item.UsuarioLogin).ThenInclude(item => item.Perfil)
            .Include(item => item.UsuarioLogin).ThenInclude(item => item.Clinica)
            .FirstOrDefaultAsync(item => item.Id == currentUser.EquipeId.Value && item.Ativa, cancellationToken);
        var op = await context.EquipeOperadores.Include(item => item.User)
            .FirstOrDefaultAsync(item => item.Id == currentUser.EquipeOperadorId.Value
                && item.EquipeId == currentUser.EquipeId.Value && item.Ativo, cancellationToken);
        if (team == null || op?.PinHash == null || !passwordHasher.VerifyPassword(currentPin, op.PinHash))
            return TeamUseCaseResult<ChangeTeamPinResponse>.Unauthorized();

        op.PinHash = passwordHasher.HashPassword(newPin);
        op.PrecisaTrocarPin = false;
        op.TentativasFalhas = 0;
        op.BloqueadoAte = null;
        op.VersaoSessao++;
        op.DataUltimaTroca = DateTime.UtcNow;
        op.DataAtualizacao = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        var membership = await context.UsuariosClinicas.Include(item => item.UsuarioGlobal)
            .FirstAsync(item => item.UserId == team.UsuarioLoginId && item.Ativo, cancellationToken);
        var jwt = jwtTokenService.GenerateToken(membership.UsuarioGlobal, membership, team.UsuarioLogin, team, op, true);
        return TeamUseCaseResult<ChangeTeamPinResponse>.Success(new ChangeTeamPinResponse(jwt, false),
            TeamAudit.Create("team.operator.pin.change", "team-operator", op.Id, op.ClinicaId,
                new Dictionary<string, object?> { ["equipeId"] = team.Id, ["operadorId"] = op.Id }));
    }

    private static string RequireText(string? value, int maxLength, string message)
    {
        var normalized = value?.Trim();
        return !string.IsNullOrWhiteSpace(normalized) && normalized.Length <= maxLength
            ? normalized
            : throw new InvalidOperationException(message);
    }
}
