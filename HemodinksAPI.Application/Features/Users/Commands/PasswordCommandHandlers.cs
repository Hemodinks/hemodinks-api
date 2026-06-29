using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// Handler para trocar senha do usuario autenticado.
/// </summary>
public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, ChangePasswordResponse>
{
    private readonly IAppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ChangePasswordResponse> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Alterando senha do usuario: {UserId}", request.UserId);

            if (request.CurrentUser != null && request.CurrentUser.Id != request.UserId)
            {
                throw new UnauthorizedAccessException("Sem permissao para alterar senha do usuario");
            }

            if (string.IsNullOrWhiteSpace(request.NovaSenha) || request.NovaSenha.Length < 8)
            {
                throw new InvalidOperationException("A nova senha deve ter pelo menos 8 caracteres");
            }

            if (request.NovaSenha == DefaultUserPassword.Value)
            {
                throw new InvalidOperationException("A nova senha nao pode ser a senha padrao");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId && u.Ativo, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            if (!_passwordHasher.VerifyPassword(request.SenhaAtual, user.Senha))
            {
                throw new InvalidOperationException("Senha atual invalida");
            }

            if (_passwordHasher.VerifyPassword(request.NovaSenha, user.Senha))
            {
                throw new InvalidOperationException("A nova senha nao pode ser igual a senha atual");
            }

            user.Senha = _passwordHasher.HashPassword(request.NovaSenha);
            user.PrecisaTrocarSenha = false;
            user.DataAtualizacao = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return new ChangePasswordResponse
            {
                Id = user.Id,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                Message = "Senha alterada com sucesso"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao alterar senha do usuario: {UserId}", request.UserId);
            throw;
        }
    }
}

/// <summary>
/// Handler para resetar a senha do usuario para a senha padrao.
/// </summary>
public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand, ResetUserPasswordResponse>
{
    private readonly IAppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ResetUserPasswordCommandHandler> _logger;

    public ResetUserPasswordCommandHandler(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<ResetUserPasswordCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ResetUserPasswordResponse> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Resetando senha do usuario: {UserId}", request.UserId);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            user.Senha = _passwordHasher.HashPassword(DefaultUserPassword.Value);
            user.PrecisaTrocarSenha = true;
            user.DataAtualizacao = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return new ResetUserPasswordResponse
            {
                Id = user.Id,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                Message = "Senha resetada para a senha padrao"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao resetar senha do usuario: {UserId}", request.UserId);
            throw;
        }
    }
}

public class ResetUserPasswordByEmailCommandHandler : IRequestHandler<ResetUserPasswordByEmailCommand, RequestPasswordResetResponse>
{
    private readonly IAppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPasswordResetNotificationSender _passwordResetNotificationSender;
    private readonly PasswordResetOptions _options;
    private readonly ILogger<ResetUserPasswordByEmailCommandHandler> _logger;

    public ResetUserPasswordByEmailCommandHandler(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        IPasswordResetNotificationSender passwordResetNotificationSender,
        IOptions<PasswordResetOptions> options,
        ILogger<ResetUserPasswordByEmailCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _passwordResetNotificationSender = passwordResetNotificationSender;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RequestPasswordResetResponse> Handle(ResetUserPasswordByEmailCommand request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var email = request.Email.Trim();

        _logger.LogInformation("Solicitacao de reset de senha recebida para {Email}", email);

        return _options.UseEmail
            ? await HandleEmailPasswordResetAsync(email, request.RequestIp, now, cancellationToken)
            : await HandleDefaultPasswordResetAsync(email, cancellationToken);
    }

    private async Task<RequestPasswordResetResponse> HandleDefaultPasswordResetAsync(
        string email,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Reset de senha por email desabilitado. Aplicando senha padrao para {Email}", email);

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.Ativo, cancellationToken);

        if (user == null)
        {
            throw new KeyNotFoundException("Usuario nao encontrado");
        }

        user.Senha = _passwordHasher.HashPassword(DefaultUserPassword.Value);
        user.PrecisaTrocarSenha = true;
        user.DataAtualizacao = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new RequestPasswordResetResponse
        {
            Id = user.Id,
            PrecisaTrocarSenha = user.PrecisaTrocarSenha,
            Message = "Senha resetada para a senha padrao",
            Mode = PasswordResetModes.DefaultPassword
        };
    }

    private async Task<RequestPasswordResetResponse> HandleEmailPasswordResetAsync(
        string email,
        string? requestIp,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var response = PasswordResetRules.CreateRequestResponse(now);

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.Ativo, cancellationToken);

        if (user == null)
        {
            _logger.LogInformation("Solicitacao de reset ignorada porque email nao foi encontrado: {Email}", email);
            return response;
        }

        var token = PasswordResetRules.GenerateToken();
        var tokenEntity = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = PasswordResetRules.HashToken(token),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30),
            RequestIp = PasswordResetRules.TrimRequestIp(requestIp)
        };

        var activeTokens = await _context.PasswordResetTokens
            .Where(item => item.UserId == user.Id
                && item.UsedAt == null
                && item.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in activeTokens)
        {
            activeToken.UsedAt = now;
        }

        _context.PasswordResetTokens.Add(tokenEntity);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Token de reset de senha criado para usuario {UserId}", user.Id);

        try
        {
            await _passwordResetNotificationSender.SendAsync(new PasswordResetNotification(
                user.Email,
                user.Nome,
                token,
                tokenEntity.ExpiresAt), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar email de reset de senha para usuario {UserId}", user.Id);
        }

        response.Id = user.Id;
        response.Mode = PasswordResetModes.EmailToken;
        response.DebugToken = _options.ExposeTokenInResponse ? token : null;
        return response;
    }
}

public class ConfirmPasswordResetCommandHandler : IRequestHandler<ConfirmPasswordResetCommand, ResetUserPasswordResponse>
{
    private readonly IAppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ConfirmPasswordResetCommandHandler> _logger;

    public ConfirmPasswordResetCommandHandler(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<ConfirmPasswordResetCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ResetUserPasswordResponse> Handle(ConfirmPasswordResetCommand request, CancellationToken cancellationToken)
    {
        PasswordResetRules.ValidateNewPassword(request.NovaSenha);

        var tokenHash = PasswordResetRules.HashToken(request.Token);
        var now = DateTime.UtcNow;
        var resetToken = await _context.PasswordResetTokens
            .Include(item => item.User)
            .FirstOrDefaultAsync(item =>
                item.TokenHash == tokenHash
                && item.UsedAt == null
                && item.ExpiresAt > now
                && item.User.Ativo,
                cancellationToken);

        if (resetToken == null)
        {
            throw new InvalidOperationException("Token de reset invalido ou expirado");
        }

        resetToken.User.Senha = _passwordHasher.HashPassword(request.NovaSenha);
        resetToken.User.PrecisaTrocarSenha = false;
        resetToken.User.DataAtualizacao = now;
        resetToken.UsedAt = now;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Senha redefinida com token para usuario {UserId}", resetToken.UserId);

        return new ResetUserPasswordResponse
        {
            Id = resetToken.UserId,
            PrecisaTrocarSenha = resetToken.User.PrecisaTrocarSenha,
            Message = "Senha redefinida com sucesso"
        };
    }
}
