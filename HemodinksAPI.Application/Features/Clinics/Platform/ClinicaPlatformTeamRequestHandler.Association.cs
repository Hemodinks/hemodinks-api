using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformTeamRequestHandler
{
        public async Task<PlatformUseCaseResult> AddClinicTeamMember(
        int id,
        int teamId,
        AssociateClinicTeamMembersRequest request,
        PlatformRequestContext requestContext,
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
        return PlatformUseCaseResult.BadRequest(new { message = "Selecione entre 1 e 100 funcionarios" });
        }
        var team = await context.Equipes
        .Include(item => item.UsuarioLogin)
        .FirstOrDefaultAsync(item => item.Id == teamId && item.ClinicaId == id && item.Ativa, cancellationToken);
        if (team == null) return PlatformUseCaseResult.NotFound();
        if (request.GerarPin && !team.ModoIdentificacao.Equals(EquipeModosIdentificacao.Pin, StringComparison.OrdinalIgnoreCase))
        {
        return PlatformUseCaseResult.BadRequest(new { message = "Ative o modo de identificacao por PIN antes de gerar um PIN individual" });
        }
        
        var sourceMemberships = await context.UsuariosClinicas
        .Include(item => item.UsuarioGlobal)
        .Include(item => item.User)
        .Where(item => globalUserIds.Contains(item.UsuarioGlobalId)
        && item.ClinicaId == id
        && item.Ativo
        && item.UsuarioGlobal.Ativo
        && item.User.Ativo
        && !context.Equipes.Any(team => team.UsuarioLoginId == item.UserId)
        && (item.PerfilId == Perfil.MedicosId
        || item.PerfilId == Perfil.ControllerId))
        .ToListAsync(cancellationToken);
        var sources = sourceMemberships
        .GroupBy(item => item.UsuarioGlobalId)
        .ToDictionary(group => group.Key, group => group.First());
        if (sources.Count != globalUserIds.Length)
        {
        return PlatformUseCaseResult.BadRequest(new { message = "A selecao deve conter apenas medicos ou controllers ativos desta clinica" });
        }
        
        var localUsers = await context.Users
        .Where(item => localUserIds.Contains(item.Id)
        && item.ClinicaId == id
        && item.Ativo
        && item.PerfilId == Perfil.EquipeId
        && !context.Equipes.Any(team => team.UsuarioLoginId == item.Id))
        .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (localUsers.Count != localUserIds.Length)
        {
        return PlatformUseCaseResult.BadRequest(new { message = "Os usuarios locais selecionados devem possuir perfil Equipe e estar ativos nesta clinica" });
        }
        
        var targetMemberships = await context.UsuariosClinicas
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
        return PlatformUseCaseResult.Conflict(new { message = "Limite de usuarios da clinica insuficiente para importar os funcionarios selecionados" });
        }
        }
        
        var targetUserIds = targetMemberships.Values.Select(item => item.UserId)
        .Concat(localUserIds)
        .Distinct()
        .ToArray();
        var existingMembers = await context.EquipeMembros
        .Where(item => item.ClinicaId == id && item.EquipeId == teamId && targetUserIds.Contains(item.UserId))
        .ToDictionaryAsync(item => item.UserId, cancellationToken);
        var existingOperators = await context.EquipeOperadores
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
        return PlatformUseCaseResult.BadRequest(new { message = $"O usuario {user.Nome} esta inativo nesta clinica" });
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
        await auditService.RecordAsync(requestContext, "team.member.add", "team-member", association.User.Id.ToString(), id,
        new { equipeId = teamId, userId = association.User.Id, pinGenerated = association.Pin != null, imported = association.Imported }, true, cancellationToken);
        }
        return PlatformUseCaseResult.Ok(new
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
}
