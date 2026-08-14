using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Teams;

public sealed class TeamUseCases(
    ITeamDbContext context,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    ILicencaService licencaService)
{
    public Task<List<TeamResponse>> ListAsync(CancellationToken cancellationToken) =>
        context.Equipes.AsNoTracking().OrderBy(item => item.Nome)
            .Select(item => new TeamResponse(item.Id, item.Nome, item.UsuarioLoginId, item.UsuarioLogin.Email,
                item.ModoIdentificacao, item.Ativa,
                item.Membros.Where(member => member.Ativo).Select(member => new TeamMemberResponse(
                    member.UserId, member.User.Nome, member.User.Email, member.User.PerfilId,
                    item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.Id).FirstOrDefault(),
                    item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.Ativo).FirstOrDefault(),
                    item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.PinHash != null).FirstOrDefault(),
                    item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.PrecisaTrocarPin).FirstOrDefault(),
                    item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.BloqueadoAte).FirstOrDefault()
                )).ToList()))
            .ToListAsync(cancellationToken);

    public async Task<TeamUseCaseResult<int>> CreateAsync(
        CurrentUserContext currentUser,
        CreateTeamInput input,
        CancellationToken cancellationToken)
    {
        var name = RequireText(input.Name, 120, "Nome da equipe obrigatorio");
        var email = GlobalIdentityService.NormalizeEmail(RequireText(input.Email, 255, "Email da equipe obrigatorio"));
        var password = RequireText(input.Password, 200, "Senha da equipe obrigatoria");
        if (password.Length < 8) return TeamUseCaseResult<int>.BadRequest("Senha da equipe deve possuir ao menos 8 caracteres");
        if (await context.Users.AnyAsync(item => item.Email == email, cancellationToken))
            return TeamUseCaseResult<int>.Conflict("Email da equipe ja cadastrado nesta clinica");
        if (await context.UsuariosGlobais.AnyAsync(item => item.Email == email, cancellationToken))
            return TeamUseCaseResult<int>.Conflict("Email coletivo ja utilizado por outra identidade");

        var user = new User
        {
            ClinicaId = currentUser.ClinicaId,
            Nome = name,
            Email = email,
            Telefone = string.IsNullOrWhiteSpace(input.Phone) ? $"+558{DateTime.UtcNow.Ticks % 10_000_000_000:D10}" : input.Phone.Trim(),
            Senha = passwordHasher.HashPassword(password),
            PerfilId = Perfil.EquipeId,
            Ativo = true,
            PrecisaTrocarSenha = false,
            DataCadastro = DateTime.UtcNow
        };
        var team = new Equipe
        {
            ClinicaId = currentUser.ClinicaId,
            Nome = name,
            UsuarioLogin = user,
            ModoIdentificacao = EquipeAuthenticationRules.NormalizeModo(input.IdentificationMode),
            Ativa = true
        };
        context.Users.Add(user);
        context.Equipes.Add(team);
        await context.SaveChangesAsync(cancellationToken);
        await GlobalIdentityService.EnsureForUserAsync(context, user, cancellationToken);
        return TeamUseCaseResult<int>.Success(team.Id, TeamAudit.Create("team.create", "team", team.Id, team.ClinicaId,
            new Dictionary<string, object?> { ["nome"] = team.Nome, ["modoIdentificacao"] = team.ModoIdentificacao, ["usuarioLoginId"] = team.UsuarioLoginId }));
    }

    public async Task<TeamUseCaseResult> UpdateAsync(int id, UpdateTeamInput input, CancellationToken cancellationToken)
    {
        var team = await context.Equipes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (team == null) return TeamUseCaseResult.NotFound();
        if (input.Name != null) team.Nome = RequireText(input.Name, 120, "Nome da equipe invalido");
        if (input.IdentificationMode != null) team.ModoIdentificacao = EquipeAuthenticationRules.NormalizeModo(input.IdentificationMode);
        if (input.Active.HasValue) team.Ativa = input.Active.Value;
        team.VersaoSessao++;
        team.DataAtualizacao = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return TeamUseCaseResult.Success(TeamAudit.Create("team.update", "team", team.Id, team.ClinicaId,
            new Dictionary<string, object?> { ["nome"] = team.Nome, ["modoIdentificacao"] = team.ModoIdentificacao, ["ativa"] = team.Ativa }));
    }

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

public enum TeamUseCaseStatus { Success, NotFound, BadRequest, Conflict, Unauthorized, Forbidden }
public record TeamUseCaseResult(TeamUseCaseStatus Status, string? Message = null, TeamAudit? Audit = null)
{
    public static TeamUseCaseResult Success(TeamAudit? audit = null) => new(TeamUseCaseStatus.Success, Audit: audit);
    public static TeamUseCaseResult NotFound() => new(TeamUseCaseStatus.NotFound);
}
public sealed record TeamUseCaseResult<T>(TeamUseCaseStatus Status, T Value = default!, string? Message = null, TeamAudit? Audit = null)
{
    public static TeamUseCaseResult<T> Success(T value, TeamAudit? audit = null) => new(TeamUseCaseStatus.Success, value, Audit: audit);
    public static TeamUseCaseResult<T> NotFound() => new(TeamUseCaseStatus.NotFound);
    public static TeamUseCaseResult<T> BadRequest(string message) => new(TeamUseCaseStatus.BadRequest, Message: message);
    public static TeamUseCaseResult<T> Conflict(string message) => new(TeamUseCaseStatus.Conflict, Message: message);
    public static TeamUseCaseResult<T> Unauthorized() => new(TeamUseCaseStatus.Unauthorized);
    public static TeamUseCaseResult<T> Forbidden() => new(TeamUseCaseStatus.Forbidden);
}
public sealed record TeamAudit(string Action, string Resource, string EntityId, int ClinicId, object Details)
{
    public static TeamAudit Create(string action, string resource, int entityId, int clinicId, object details) =>
        new(action, resource, entityId.ToString(), clinicId, details);
}
public sealed record CreateTeamInput(string Name, string Email, string Password, string? Phone, string? IdentificationMode);
public sealed record UpdateTeamInput(string? Name, string? IdentificationMode, bool? Active);
public sealed record AssociateTeamMemberResponse(int Id, string? PinTemporario);
public sealed record ChangeTeamPinResponse(string Token, bool PrecisaTrocarPin);
public sealed record TeamResponse(int Id, string Nome, int UsuarioLoginId, string Email, string ModoIdentificacao,
    bool Ativa, IReadOnlyList<TeamMemberResponse> Membros);
public sealed record TeamMemberResponse(int UserId, string Nome, string Email, int PerfilId, int OperadorId,
    bool OperadorAtivo, bool PossuiPin, bool PrecisaTrocarPin, DateTime? BloqueadoAte);
