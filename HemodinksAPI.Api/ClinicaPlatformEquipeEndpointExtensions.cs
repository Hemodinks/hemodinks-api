using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public static partial class ClinicaPlatformEndpointExtensions
{
    private static async Task<IResult> ListClinicTeams(
        int id,
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        if (!await context.Clinicas.AsNoTracking().AnyAsync(item => item.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var teams = await context.Equipes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.ClinicaId == id)
            .OrderBy(item => item.Nome)
            .Select(item => new EquipeResponse(
                item.Id,
                item.Nome,
                item.UsuarioLoginId,
                item.UsuarioLogin.Email,
                item.ModoIdentificacao,
                item.Ativa,
                item.Membros.Where(member => member.Ativo).Select(member => new EquipeMembroResponse(
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

        return Results.Ok(teams);
    }

    private static async Task<IResult> ListClinicTeamUsers(
        int id,
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        if (!await context.Clinicas.AsNoTracking().AnyAsync(item => item.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var eligibleMemberships = await context.UsuariosClinicas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Ativo
                && item.UsuarioGlobal.Ativo
                && item.User.Ativo
                && (item.PerfilId == Perfil.MedicosId || item.PerfilId == Perfil.ControllerId))
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
            .IgnoreQueryFilters()
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
            .OrderBy(item => item.Nome)
            .ToList();

        return Results.Ok(candidates);
    }

    private static async Task<IResult> UpdateClinicTeam(
        int id,
        int teamId,
        AtualizarEquipeRequest request,
        HttpContext httpContext,
        AppDbContext context,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var team = await context.Equipes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == teamId && item.ClinicaId == id, cancellationToken);
        if (team == null) return Results.NotFound();

        if (request.Nome != null) team.Nome = RequireText(request.Nome, "Nome da equipe invalido", 120);
        if (request.ModoIdentificacao != null) team.ModoIdentificacao = EquipeAuthenticationRules.NormalizeModo(request.ModoIdentificacao);
        if (request.Ativa.HasValue) team.Ativa = request.Ativa.Value;
        team.VersaoSessao++;
        team.DataAtualizacao = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(httpContext, "team.update", "team", team.Id.ToString(), id,
            new { team.Nome, team.ModoIdentificacao, team.Ativa }, true, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> AddClinicTeamMember(
        int id,
        int teamId,
        AssociateClinicTeamMembersRequest request,
        HttpContext httpContext,
        AppDbContext context,
        IPasswordHasher passwordHasher,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var globalUserIds = request.UsuarioGlobalIds?.Distinct().ToArray() ?? [];
        if (globalUserIds.Length == 0 || globalUserIds.Length > 100)
        {
            return Results.BadRequest(new { message = "Selecione entre 1 e 100 funcionarios" });
        }

        var team = await context.Equipes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == teamId && item.ClinicaId == id && item.Ativa, cancellationToken);
        if (team == null) return Results.NotFound();
        if (request.GerarPin && !team.ModoIdentificacao.Equals(EquipeModosIdentificacao.Pin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "Ative o modo de identificacao por PIN antes de gerar um PIN individual" });
        }

        var sourceMemberships = await context.UsuariosClinicas
            .IgnoreQueryFilters()
            .Include(item => item.UsuarioGlobal)
            .Include(item => item.User)
            .Where(item => globalUserIds.Contains(item.UsuarioGlobalId)
                && item.Ativo
                && item.UsuarioGlobal.Ativo
                && item.User.Ativo
                && (item.PerfilId == Perfil.MedicosId || item.PerfilId == Perfil.ControllerId))
            .ToListAsync(cancellationToken);
        var sources = sourceMemberships
            .GroupBy(item => item.UsuarioGlobalId)
            .ToDictionary(group => group.Key, group => group.First());
        if (sources.Count != globalUserIds.Length)
        {
            return Results.BadRequest(new { message = "A selecao deve conter apenas medicos e controllers ativos" });
        }

        var targetMemberships = await context.UsuariosClinicas
            .IgnoreQueryFilters()
            .Include(item => item.User)
            .Where(item => item.ClinicaId == id && globalUserIds.Contains(item.UsuarioGlobalId))
            .ToDictionaryAsync(item => item.UsuarioGlobalId, cancellationToken);
        var importCount = globalUserIds.Count(globalUserId => !targetMemberships.ContainsKey(globalUserId));
        var clinicLimit = await context.Clinicas.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => item.LimiteUsuarios)
            .FirstAsync(cancellationToken);
        if (clinicLimit.HasValue && importCount > 0)
        {
            var currentUserCount = await context.Users.IgnoreQueryFilters()
                .CountAsync(item => item.ClinicaId == id, cancellationToken);
            if (currentUserCount + importCount > clinicLimit.Value)
            {
                return Results.Conflict(new { message = "Limite de usuarios da clinica insuficiente para importar os funcionarios selecionados" });
            }
        }

        var targetUserIds = targetMemberships.Values.Select(item => item.UserId).ToArray();
        var existingMembers = await context.EquipeMembros.IgnoreQueryFilters()
            .Where(item => item.ClinicaId == id && item.EquipeId == teamId && targetUserIds.Contains(item.UserId))
            .ToDictionaryAsync(item => item.UserId, cancellationToken);
        var existingOperators = await context.EquipeOperadores.IgnoreQueryFilters()
            .Where(item => item.ClinicaId == id && item.EquipeId == teamId && targetUserIds.Contains(item.UserId))
            .ToDictionaryAsync(item => item.UserId, cancellationToken);
        var associations = new List<(User User, EquipeOperador Operator, string? Pin, bool Imported)>();

        foreach (var globalUserId in globalUserIds)
        {
            var source = sources[globalUserId];
            var imported = !targetMemberships.TryGetValue(globalUserId, out var targetMembership);
            User user;
            if (imported)
            {
                user = new User
                {
                    ClinicaId = id,
                    Nome = source.UsuarioGlobal.Nome,
                    Email = source.UsuarioGlobal.Email,
                    Telefone = source.User.Telefone,
                    FotoPerfil = source.User.FotoPerfil,
                    Senha = source.UsuarioGlobal.Senha,
                    DataNascimento = source.User.DataNascimento,
                    DataCadastro = DateTime.UtcNow,
                    Ativo = true,
                    PrecisaTrocarSenha = false,
                    PerfilId = Perfil.EquipeId
                };
                context.Users.Add(user);
                context.UsuariosClinicas.Add(new UsuarioClinica
                {
                    UsuarioGlobalId = globalUserId,
                    ClinicaId = id,
                    User = user,
                    PerfilId = Perfil.EquipeId,
                    Ativo = true,
                    ClinicaPadrao = false,
                    DataCadastro = DateTime.UtcNow
                });
            }
            else
            {
                user = targetMembership!.User;
                if (!user.Ativo || !targetMembership.Ativo)
                {
                    return Results.BadRequest(new { message = $"O usuario {user.Nome} esta inativo nesta clinica" });
                }
            }

            EquipeMembro member;
            if (user.Id != 0 && existingMembers.TryGetValue(user.Id, out var existingMember))
            {
                member = existingMember;
                member.Ativo = true;
                member.DataAtualizacao = DateTime.UtcNow;
            }
            else
            {
                member = new EquipeMembro { ClinicaId = id, EquipeId = teamId, User = user };
                context.EquipeMembros.Add(member);
            }

            EquipeOperador op;
            if (user.Id != 0 && existingOperators.TryGetValue(user.Id, out var existingOperator))
            {
                op = existingOperator;
            }
            else
            {
                op = new EquipeOperador { ClinicaId = id, EquipeId = teamId, User = user };
                context.EquipeOperadores.Add(op);
            }
            op.Ativo = true;
            op.VersaoSessao++;

            string? temporaryPin = null;
            if (request.GerarPin)
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
            associations.Add((user, op, temporaryPin, imported));
        }

        team.VersaoSessao++;
        await context.SaveChangesAsync(cancellationToken);
        foreach (var association in associations)
        {
            await auditService.RecordAsync(httpContext, "team.member.add", "team-member", association.User.Id.ToString(), id,
                new { equipeId = teamId, userId = association.User.Id, pinGenerated = association.Pin != null, imported = association.Imported }, true, cancellationToken);
        }
        return Results.Ok(new
        {
            Associados = associations.Select(item => new
            {
                UserId = item.User.Id,
                item.User.Nome,
                OperadorId = item.Operator.Id,
                PinTemporario = item.Pin,
                Importado = item.Imported
            })
        });
    }

    private static async Task<IResult> RemoveClinicTeamMember(
        int id,
        int teamId,
        int userId,
        HttpContext httpContext,
        AppDbContext context,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var team = await context.Equipes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == teamId && item.ClinicaId == id, cancellationToken);
        var member = await context.EquipeMembros.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.ClinicaId == id && item.EquipeId == teamId && item.UserId == userId, cancellationToken);
        if (team == null || member == null) return Results.NotFound();

        member.Ativo = false;
        member.DataAtualizacao = DateTime.UtcNow;
        var op = await context.EquipeOperadores.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.ClinicaId == id && item.EquipeId == teamId && item.UserId == userId, cancellationToken);
        if (op != null)
        {
            op.Ativo = false;
            op.VersaoSessao++;
            op.DataAtualizacao = DateTime.UtcNow;
        }
        team.VersaoSessao++;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(httpContext, "team.member.remove", "team-member", userId.ToString(), id,
            new { equipeId = teamId, userId }, true, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetClinicTeamOperatorPin(
        int id,
        int teamId,
        int operatorId,
        HttpContext httpContext,
        AppDbContext context,
        IPasswordHasher passwordHasher,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var team = await context.Equipes.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == teamId && item.ClinicaId == id && item.Ativa, cancellationToken);
        if (team == null) return Results.NotFound();
        if (!team.ModoIdentificacao.Equals(EquipeModosIdentificacao.Pin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "A equipe nao utiliza identificacao por PIN" });
        }

        var op = await context.EquipeOperadores.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == operatorId && item.EquipeId == teamId && item.ClinicaId == id && item.Ativo, cancellationToken);
        if (op == null) return Results.NotFound();
        var pin = EquipeAuthenticationRules.GeneratePin();
        op.PinHash = passwordHasher.HashPassword(pin);
        op.PrecisaTrocarPin = true;
        op.TentativasFalhas = 0;
        op.BloqueadoAte = null;
        op.VersaoSessao++;
        op.DataUltimaTroca = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(httpContext, "team.operator.pin.reset", "team-operator", op.Id.ToString(), id,
            new { equipeId = teamId, operadorId = operatorId }, true, cancellationToken);
        return Results.Ok(new { PinTemporario = pin });
    }
}

public sealed record ClinicTeamUserResponse(
    int UsuarioGlobalId,
    int? UserIdNaClinica,
    string Nome,
    string Email,
    int PerfilId,
    string PerfilNome,
    string OrigemClinica,
    bool CadastradoNaClinica);

public sealed record AssociateClinicTeamMembersRequest(IReadOnlyList<int>? UsuarioGlobalIds, bool GerarPin);
