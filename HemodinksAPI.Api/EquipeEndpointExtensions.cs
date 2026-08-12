using System.Security.Claims;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public static class EquipeEndpointExtensions
{
    public static void MapEquipeEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/equipes")
            .WithTags("Equipes")
            .RequireAuthorization("Administrador");

        admin.MapGet("/", Listar);
        admin.MapPost("/", Criar);
        admin.MapPut("/{id:int}", Atualizar);
        admin.MapPost("/{id:int}/membros", AssociarMembro);
        admin.MapDelete("/{id:int}/membros/{userId:int}", DesassociarMembro);
        admin.MapPost("/{id:int}/operadores/{operadorId:int}/pin", RedefinirPin);
        admin.MapPut("/{id:int}/operadores/{operadorId:int}/bloqueio", AlterarBloqueio);

        app.MapPost("/api/equipe-auth/identificar", IdentificarOperador)
            .WithTags("Equipes - Autenticacao")
            .AllowAnonymous()
            .RequireRateLimiting("PasswordReset");

        app.MapPut("/api/equipe-auth/pin", TrocarPin)
            .WithTags("Equipes - Autenticacao")
            .RequireAuthorization("Equipe");
    }

    private static async Task<IResult> Listar(AppDbContext context, CancellationToken cancellationToken)
    {
        var equipes = await context.Equipes
            .AsNoTracking()
            .OrderBy(item => item.Nome)
            .Select(item => new EquipeResponse(
                item.Id,
                item.Nome,
                item.UsuarioLoginId,
                item.UsuarioLogin.Email,
                item.ModoIdentificacao,
                item.Ativa,
                item.Membros.Where(membro => membro.Ativo).Select(membro => new EquipeMembroResponse(
                    membro.UserId,
                    membro.User.Nome,
                    membro.User.Email,
                    membro.User.PerfilId,
                    item.Operadores.Where(operador => operador.UserId == membro.UserId).Select(operador => operador.Id).FirstOrDefault(),
                    item.Operadores.Where(operador => operador.UserId == membro.UserId).Select(operador => operador.Ativo).FirstOrDefault(),
                    item.Operadores.Where(operador => operador.UserId == membro.UserId).Select(operador => operador.PinHash != null).FirstOrDefault(),
                    item.Operadores.Where(operador => operador.UserId == membro.UserId).Select(operador => operador.PrecisaTrocarPin).FirstOrDefault(),
                    item.Operadores.Where(operador => operador.UserId == membro.UserId).Select(operador => operador.BloqueadoAte).FirstOrDefault()
                )).ToList()))
            .ToListAsync(cancellationToken);
        return Results.Ok(equipes);
    }

    private static async Task<IResult> Criar(
        CriarEquipeRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        AppDbContext context,
        IPasswordHasher passwordHasher,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var currentUser = GetCurrentUser(principal);
        var nome = RequireText(request.Nome, 120, "Nome da equipe obrigatorio");
        var email = GlobalIdentityService.NormalizeEmail(RequireText(request.Email, 255, "Email da equipe obrigatorio"));
        var password = RequireText(request.Senha, 200, "Senha da equipe obrigatoria");
        if (password.Length < 8)
        {
            return Results.BadRequest(new { message = "Senha da equipe deve possuir ao menos 8 caracteres" });
        }

        if (await context.Users.AnyAsync(item => item.Email == email, cancellationToken))
        {
            return Results.Conflict(new { message = "Email da equipe ja cadastrado nesta clinica" });
        }

        if (await context.UsuariosGlobais.AnyAsync(item => item.Email == email, cancellationToken))
        {
            return Results.Conflict(new { message = "Email coletivo ja utilizado por outra identidade" });
        }

        var user = new User
        {
            ClinicaId = currentUser.ClinicaId,
            Nome = nome,
            Email = email,
            Telefone = string.IsNullOrWhiteSpace(request.Telefone) ? $"+558{DateTime.UtcNow.Ticks % 10_000_000_000:D10}" : request.Telefone.Trim(),
            Senha = passwordHasher.HashPassword(password),
            PerfilId = Perfil.EquipeId,
            Ativo = true,
            PrecisaTrocarSenha = false,
            DataCadastro = DateTime.UtcNow
        };
        var equipe = new Equipe
        {
            ClinicaId = currentUser.ClinicaId,
            Nome = nome,
            UsuarioLogin = user,
            ModoIdentificacao = EquipeAuthenticationRules.NormalizeModo(request.ModoIdentificacao),
            Ativa = true
        };
        context.Users.Add(user);
        context.Equipes.Add(equipe);
        await context.SaveChangesAsync(cancellationToken);
        await GlobalIdentityService.EnsureForUserAsync(context, user, cancellationToken);

        await auditService.RecordAsync(httpContext, "team.create", "team", equipe.Id.ToString(), equipe.ClinicaId,
            new { equipe.Nome, equipe.ModoIdentificacao, equipe.UsuarioLoginId }, true, cancellationToken);
        return Results.Created($"/api/equipes/{equipe.Id}", new { equipe.Id });
    }

    private static async Task<IResult> Atualizar(
        int id,
        AtualizarEquipeRequest request,
        HttpContext httpContext,
        AppDbContext context,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var equipe = await context.Equipes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (equipe == null) return Results.NotFound();
        if (request.Nome != null) equipe.Nome = RequireText(request.Nome, 120, "Nome da equipe invalido");
        if (request.ModoIdentificacao != null) equipe.ModoIdentificacao = EquipeAuthenticationRules.NormalizeModo(request.ModoIdentificacao);
        if (request.Ativa.HasValue) equipe.Ativa = request.Ativa.Value;
        equipe.VersaoSessao++;
        equipe.DataAtualizacao = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(httpContext, "team.update", "team", equipe.Id.ToString(), equipe.ClinicaId,
            new { equipe.Nome, equipe.ModoIdentificacao, equipe.Ativa }, true, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> AssociarMembro(
        int id,
        AssociarEquipeMembroRequest request,
        HttpContext httpContext,
        AppDbContext context,
        IPasswordHasher passwordHasher,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var equipe = await context.Equipes.FirstOrDefaultAsync(item => item.Id == id && item.Ativa, cancellationToken);
        var user = await context.Users.FirstOrDefaultAsync(item => item.Id == request.UserId
            && item.Ativo
            && (item.PerfilId == Perfil.MedicosId || item.PerfilId == Perfil.ControllerId), cancellationToken);
        if (equipe == null || user == null) return Results.NotFound();
        if (request.GerarPin && !equipe.ModoIdentificacao.Equals(EquipeModosIdentificacao.Pin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "Ative o modo de identificacao por PIN antes de gerar um PIN individual" });
        }

        var membro = await context.EquipeMembros.FirstOrDefaultAsync(item => item.EquipeId == id && item.UserId == user.Id, cancellationToken);
        if (membro == null)
        {
            membro = new EquipeMembro { ClinicaId = equipe.ClinicaId, EquipeId = id, UserId = user.Id };
            context.EquipeMembros.Add(membro);
        }
        else
        {
            membro.Ativo = true;
            membro.DataAtualizacao = DateTime.UtcNow;
        }

        var operador = await context.EquipeOperadores.FirstOrDefaultAsync(item => item.EquipeId == id && item.UserId == user.Id, cancellationToken);
        if (operador == null)
        {
            operador = new EquipeOperador { ClinicaId = equipe.ClinicaId, EquipeId = id, UserId = user.Id };
            context.EquipeOperadores.Add(operador);
        }
        operador.Ativo = true;
        operador.VersaoSessao++;

        string? pinTemporario = null;
        if (request.GerarPin)
        {
            pinTemporario = EquipeAuthenticationRules.GeneratePin();
            operador.PinHash = passwordHasher.HashPassword(pinTemporario);
            operador.PrecisaTrocarPin = true;
            operador.DataUltimaTroca = DateTime.UtcNow;
        }
        else
        {
            operador.PinHash = null;
            operador.PrecisaTrocarPin = false;
            operador.TentativasFalhas = 0;
            operador.BloqueadoAte = null;
        }

        equipe.VersaoSessao++;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(httpContext, "team.member.add", "team-member", user.Id.ToString(), equipe.ClinicaId,
            new { equipeId = equipe.Id, userId = user.Id, pinGenerated = pinTemporario != null }, true, cancellationToken);
        return Results.Ok(new { operador.Id, PinTemporario = pinTemporario });
    }

    private static async Task<IResult> DesassociarMembro(
        int id,
        int userId,
        HttpContext httpContext,
        AppDbContext context,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var membro = await context.EquipeMembros.FirstOrDefaultAsync(item => item.EquipeId == id && item.UserId == userId, cancellationToken);
        if (membro == null) return Results.NotFound();
        membro.Ativo = false;
        membro.DataAtualizacao = DateTime.UtcNow;
        var operador = await context.EquipeOperadores.FirstOrDefaultAsync(item => item.EquipeId == id && item.UserId == userId, cancellationToken);
        if (operador != null)
        {
            operador.Ativo = false;
            operador.VersaoSessao++;
            operador.DataAtualizacao = DateTime.UtcNow;
        }
        var equipe = await context.Equipes.FirstAsync(item => item.Id == id, cancellationToken);
        equipe.VersaoSessao++;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(httpContext, "team.member.remove", "team-member", userId.ToString(), equipe.ClinicaId,
            new { equipeId = id, userId }, true, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RedefinirPin(
        int id,
        int operadorId,
        HttpContext httpContext,
        AppDbContext context,
        IPasswordHasher passwordHasher,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var equipe = await context.Equipes.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id && item.Ativa, cancellationToken);
        if (equipe == null || !equipe.ModoIdentificacao.Equals(EquipeModosIdentificacao.Pin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "A equipe nao utiliza identificacao por PIN" });
        }
        var operador = await context.EquipeOperadores.FirstOrDefaultAsync(item => item.Id == operadorId && item.EquipeId == id && item.Ativo, cancellationToken);
        if (operador == null) return Results.NotFound();
        var pin = EquipeAuthenticationRules.GeneratePin();
        operador.PinHash = passwordHasher.HashPassword(pin);
        operador.PrecisaTrocarPin = true;
        operador.TentativasFalhas = 0;
        operador.BloqueadoAte = null;
        operador.VersaoSessao++;
        operador.DataUltimaTroca = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(httpContext, "team.operator.pin.reset", "team-operator", operador.Id.ToString(), operador.ClinicaId,
            new { equipeId = id, operadorId }, true, cancellationToken);
        return Results.Ok(new { PinTemporario = pin });
    }

    private static async Task<IResult> AlterarBloqueio(
        int id,
        int operadorId,
        AlterarBloqueioOperadorRequest request,
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var operador = await context.EquipeOperadores.FirstOrDefaultAsync(item => item.Id == operadorId && item.EquipeId == id, cancellationToken);
        if (operador == null) return Results.NotFound();
        operador.Ativo = !request.Bloqueado;
        operador.BloqueadoAte = request.Bloqueado ? DateTime.MaxValue : null;
        operador.TentativasFalhas = 0;
        operador.VersaoSessao++;
        await context.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> IdentificarOperador(
        IdentificarEquipeRequest request,
        AppDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ILicencaService licencaService,
        CancellationToken cancellationToken)
    {
        var tokenHash = EquipeAuthenticationRules.HashChallengeToken(request.Token);
        var desafio = await context.EquipeLoginDesafios
            .Include(item => item.Equipe).ThenInclude(item => item.UsuarioLogin).ThenInclude(item => item.Perfil)
            .Include(item => item.Equipe).ThenInclude(item => item.UsuarioLogin).ThenInclude(item => item.Clinica)
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash && item.UtilizadoEm == null && item.ExpiraEm > DateTime.UtcNow, cancellationToken);
        if (desafio == null || !desafio.Equipe.Ativa) return Results.Unauthorized();

        var operador = await context.EquipeOperadores
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.Id == request.OperadorId && item.EquipeId == desafio.EquipeId && item.Ativo, cancellationToken);
        if (operador == null || operador.BloqueadoAte > DateTime.UtcNow
            || !await context.EquipeMembros.AnyAsync(item => item.EquipeId == desafio.EquipeId && item.UserId == operador.UserId && item.Ativo, cancellationToken))
        {
            return Results.Unauthorized();
        }

        var exigePin = desafio.Equipe.ModoIdentificacao.Equals(EquipeModosIdentificacao.Pin, StringComparison.OrdinalIgnoreCase)
            && operador.PinHash != null;
        if (exigePin && (operador.PinHash == null || !passwordHasher.VerifyPassword(request.Pin ?? string.Empty, operador.PinHash)))
        {
            operador.TentativasFalhas++;
            if (operador.TentativasFalhas >= 5)
            {
                operador.BloqueadoAte = DateTime.UtcNow.AddMinutes(15);
                operador.TentativasFalhas = 0;
                operador.VersaoSessao++;
            }
            await context.SaveChangesAsync(cancellationToken);
            return Results.Unauthorized();
        }

        operador.TentativasFalhas = 0;
        operador.BloqueadoAte = null;
        desafio.UtilizadoEm = DateTime.UtcNow;
        var membership = await context.UsuariosClinicas
            .Include(item => item.UsuarioGlobal)
            .FirstAsync(item => item.UserId == desafio.Equipe.UsuarioLoginId && item.Ativo, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var loginUser = desafio.Equipe.UsuarioLogin;
        var token = jwtTokenService.GenerateToken(membership.UsuarioGlobal, membership, loginUser, desafio.Equipe, operador, exigePin);
        var licenca = await licencaService.GetCurrentAsync(new CurrentUserContext(
            loginUser.Id, loginUser.PerfilId, operador.User.Nome, loginUser.ClinicaId, loginUser.Clinica.Slug,
            membership.UsuarioGlobalId, membership.Id, desafio.EquipeId, operador.Id, exigePin), cancellationToken);

        return Results.Ok(new AuthenticateUserResponse
        {
            Id = loginUser.Id,
            UsuarioGlobalId = membership.UsuarioGlobalId,
            ClinicaId = loginUser.ClinicaId,
            ClinicaSlug = loginUser.Clinica.Slug,
            Nome = operador.User.Nome,
            Email = membership.UsuarioGlobal.Email,
            Token = token,
            PrecisaTrocarSenha = loginUser.PrecisaTrocarSenha,
            PrecisaTrocarPin = exigePin && operador.PrecisaTrocarPin,
            PerfilId = Perfil.EquipeId,
            PerfilNome = "Equipe",
            ModulosLiberados = ClinicaModulos.GetEffective(loginUser.Clinica.Plano, loginUser.Clinica.ModulosLiberados),
            Licenca = licenca
        });
    }

    private static async Task<IResult> TrocarPin(
        TrocarEquipePinRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        AppDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var currentUser = GetCurrentUser(principal);
        if (!currentUser.IsEquipe || !currentUser.EquipeId.HasValue || !currentUser.EquipeOperadorId.HasValue)
        {
            return Results.Forbid();
        }

        if (!EquipeAuthenticationRules.IsValidPinFormat(request.PinAtual)
            || !EquipeAuthenticationRules.IsValidPinFormat(request.NovoPin))
        {
            return Results.BadRequest(new { message = "O PIN deve possuir exatamente 6 numeros" });
        }

        if (request.PinAtual == request.NovoPin)
        {
            return Results.BadRequest(new { message = "O novo PIN deve ser diferente do PIN temporario" });
        }

        var equipe = await context.Equipes
            .Include(item => item.UsuarioLogin).ThenInclude(item => item.Perfil)
            .Include(item => item.UsuarioLogin).ThenInclude(item => item.Clinica)
            .FirstOrDefaultAsync(item => item.Id == currentUser.EquipeId.Value && item.Ativa, cancellationToken);
        var operador = await context.EquipeOperadores
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.Id == currentUser.EquipeOperadorId.Value
                && item.EquipeId == currentUser.EquipeId.Value
                && item.Ativo, cancellationToken);
        if (equipe == null || operador?.PinHash == null
            || !passwordHasher.VerifyPassword(request.PinAtual, operador.PinHash))
        {
            return Results.Unauthorized();
        }

        operador.PinHash = passwordHasher.HashPassword(request.NovoPin);
        operador.PrecisaTrocarPin = false;
        operador.TentativasFalhas = 0;
        operador.BloqueadoAte = null;
        operador.VersaoSessao++;
        operador.DataUltimaTroca = DateTime.UtcNow;
        operador.DataAtualizacao = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        var membership = await context.UsuariosClinicas
            .Include(item => item.UsuarioGlobal)
            .FirstAsync(item => item.UserId == equipe.UsuarioLoginId && item.Ativo, cancellationToken);
        var token = jwtTokenService.GenerateToken(
            membership.UsuarioGlobal,
            membership,
            equipe.UsuarioLogin,
            equipe,
            operador,
            identificacaoConfiavel: true);

        await auditService.RecordAsync(httpContext, "team.operator.pin.change", "team-operator",
            operador.Id.ToString(), operador.ClinicaId, new { equipeId = equipe.Id, operadorId = operador.Id }, true, cancellationToken);
        return Results.Ok(new { Token = token, PrecisaTrocarPin = false });
    }

    private static CurrentUserContext GetCurrentUser(ClaimsPrincipal principal) =>
        principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");

    private static string RequireText(string? value, int maxLength, string message)
    {
        var normalized = value?.Trim();
        return !string.IsNullOrWhiteSpace(normalized) && normalized.Length <= maxLength
            ? normalized
            : throw new InvalidOperationException(message);
    }
}

public sealed record CriarEquipeRequest(string Nome, string Email, string Senha, string? Telefone, string? ModoIdentificacao);
public sealed record AtualizarEquipeRequest(string? Nome, string? ModoIdentificacao, bool? Ativa);
public sealed record AssociarEquipeMembroRequest(int UserId, bool GerarPin);
public sealed record AlterarBloqueioOperadorRequest(bool Bloqueado);
public sealed record IdentificarEquipeRequest(string Token, int OperadorId, string? Pin);
public sealed record TrocarEquipePinRequest(string PinAtual, string NovoPin);
public sealed record EquipeResponse(int Id, string Nome, int UsuarioLoginId, string Email, string ModoIdentificacao, bool Ativa, IReadOnlyList<EquipeMembroResponse> Membros);
public sealed record EquipeMembroResponse(int UserId, string Nome, string Email, int PerfilId, int OperadorId, bool OperadorAtivo, bool PossuiPin, bool PrecisaTrocarPin, DateTime? BloqueadoAte);
