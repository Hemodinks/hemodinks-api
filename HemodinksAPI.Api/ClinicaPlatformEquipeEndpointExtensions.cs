using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Teams;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public static partial class ClinicaPlatformEndpointExtensions
{
    private static async Task<IResult> ListClinicTeams(
        int id,
        IPlatformTeamDbContext context,
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

        return Results.Ok(teams);
    }

    private static async Task<IResult> ListClinicTeamUsers(
        int id,
        IPlatformTeamDbContext context,
        CancellationToken cancellationToken)
    {
        if (!await context.Clinicas.AsNoTracking().AnyAsync(item => item.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var eligibleMemberships = await context.UsuariosClinicas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.ClinicaId == id
                && item.Ativo
                && item.UsuarioGlobal.Ativo
                && item.User.Ativo
                && !context.Equipes.IgnoreQueryFilters().Any(team => team.UsuarioLoginId == item.UserId)
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
            .ToList();

        var localTeamUsers = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.ClinicaId == id
                && item.Ativo
                && item.PerfilId == Perfil.EquipeId
                && !context.UsuariosClinicas.IgnoreQueryFilters().Any(link => link.UserId == item.Id)
                && !context.Equipes.IgnoreQueryFilters().Any(team => team.UsuarioLoginId == item.Id))
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

        return Results.Ok(candidates);
    }

    private static async Task<IResult> UpdateClinicTeam(
        int id,
        int teamId,
        AtualizarEquipeRequest request,
        HttpContext httpContext,
        IPlatformTeamDbContext context,
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
        IPlatformTeamDbContext context,
        IPasswordHasher passwordHasher,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var globalUserIds = request.UsuarioGlobalIds?.Distinct().ToArray() ?? [];
        var localUserIds = request.UserIds?.Distinct().ToArray() ?? [];
        var newUsers = request.NovosUsuarios?
            .Select(item => new CreateClinicTeamUserRequest(
                RequireText(item.Nome, "Nome do funcionario invalido", 255),
                string.IsNullOrWhiteSpace(item.Telefone)
                    ? null
                    : RequireText(item.Telefone, "Telefone do funcionario invalido", 20)))
            .ToArray() ?? [];
        var selectionCount = globalUserIds.Length + localUserIds.Length + newUsers.Length;
        if (selectionCount == 0 || selectionCount > 100)
        {
            return Results.BadRequest(new { message = "Selecione entre 1 e 100 funcionarios" });
        }
        var team = await context.Equipes.IgnoreQueryFilters()
            .Include(item => item.UsuarioLogin)
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
                && item.ClinicaId == id
                && item.Ativo
                && item.UsuarioGlobal.Ativo
                && item.User.Ativo
                && !context.Equipes.IgnoreQueryFilters().Any(team => team.UsuarioLoginId == item.UserId)
                && (item.PerfilId == Perfil.MedicosId
                    || item.PerfilId == Perfil.ControllerId))
            .ToListAsync(cancellationToken);
        var sources = sourceMemberships
            .GroupBy(item => item.UsuarioGlobalId)
            .ToDictionary(group => group.Key, group => group.First());
        if (sources.Count != globalUserIds.Length)
        {
            return Results.BadRequest(new { message = "A selecao deve conter apenas medicos ou controllers ativos desta clinica" });
        }

        var localUsers = await context.Users.IgnoreQueryFilters()
            .Where(item => localUserIds.Contains(item.Id)
                && item.ClinicaId == id
                && item.Ativo
                && item.PerfilId == Perfil.EquipeId
                && !context.Equipes.IgnoreQueryFilters().Any(team => team.UsuarioLoginId == item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (localUsers.Count != localUserIds.Length)
        {
            return Results.BadRequest(new { message = "Os usuarios locais selecionados devem possuir perfil Equipe e estar ativos nesta clinica" });
        }

        var targetMemberships = await context.UsuariosClinicas
            .IgnoreQueryFilters()
            .Include(item => item.User)
            .Where(item => item.ClinicaId == id && globalUserIds.Contains(item.UsuarioGlobalId))
            .ToDictionaryAsync(item => item.UsuarioGlobalId, cancellationToken);
        var importCount = globalUserIds.Count(globalUserId => !targetMemberships.ContainsKey(globalUserId)) + newUsers.Length;
        var clinicLimit = await context.Clinicas.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => item.LimiteUsuarios)
            .FirstAsync(cancellationToken);
        if (clinicLimit.HasValue && importCount > 0)
        {
            var currentUserCount = await ClinicEmployees(context)
                .CountAsync(item => item.ClinicaId == id, cancellationToken);
            if (currentUserCount + importCount > clinicLimit.Value)
            {
                return Results.Conflict(new { message = "Limite de usuarios da clinica insuficiente para importar os funcionarios selecionados" });
            }
        }

        var targetUserIds = targetMemberships.Values.Select(item => item.UserId)
            .Concat(localUserIds)
            .Distinct()
            .ToArray();
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

        foreach (var localUserId in localUserIds)
        {
            var user = localUsers[localUserId];
            EquipeMembro member;
            if (existingMembers.TryGetValue(user.Id, out var existingMember))
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
            if (existingOperators.TryGetValue(user.Id, out var existingOperator))
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
            associations.Add((user, op, temporaryPin, false));
        }

        foreach (var newUserRequest in newUsers)
        {
            var user = new User
            {
                ClinicaId = id,
                Nome = newUserRequest.Nome,
                Email = team.UsuarioLogin.Email,
                Telefone = newUserRequest.Telefone ?? string.Empty,
                Senha = team.UsuarioLogin.Senha,
                DataCadastro = DateTime.UtcNow,
                Ativo = true,
                PrecisaTrocarSenha = false,
                PerfilId = Perfil.EquipeId
            };
            context.Users.Add(user);
            context.EquipeMembros.Add(new EquipeMembro
            {
                ClinicaId = id,
                EquipeId = teamId,
                User = user
            });
            var op = new EquipeOperador
            {
                ClinicaId = id,
                EquipeId = teamId,
                User = user,
                Ativo = true
            };
            context.EquipeOperadores.Add(op);

            string? temporaryPin = null;
            if (request.GerarPin)
            {
                temporaryPin = EquipeAuthenticationRules.GeneratePin();
                op.PinHash = passwordHasher.HashPassword(temporaryPin);
                op.PrecisaTrocarPin = true;
                op.DataUltimaTroca = DateTime.UtcNow;
            }
            associations.Add((user, op, temporaryPin, true));
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
        IPlatformTeamDbContext context,
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
        IPlatformTeamDbContext context,
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
    int? UsuarioGlobalId,
    int? UserIdNaClinica,
    string Nome,
    string Email,
    int PerfilId,
    string PerfilNome,
    string OrigemClinica,
    bool CadastradoNaClinica);

public sealed record AssociateClinicTeamMembersRequest(
    IReadOnlyList<int>? UsuarioGlobalIds,
    IReadOnlyList<int>? UserIds,
    IReadOnlyList<CreateClinicTeamUserRequest>? NovosUsuarios,
    bool GerarPin);

public sealed record CreateClinicTeamUserRequest(string Nome, string? Telefone);
