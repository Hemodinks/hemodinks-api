using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Utils;
using MediatR;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Application.Features.Users.Commands;

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
            : await HandleDefaultPasswordResetAsync(email, now, cancellationToken);
    }

    private async Task<RequestPasswordResetResponse> HandleDefaultPasswordResetAsync(
        string email,
        DateTime now,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Reset de senha por email desabilitado. Aplicando senha padrao para {Email}", email);

        var user = await PasswordCommandQueries.GetActiveUserByEmailAsync(_context, email, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException("Usuario nao encontrado");
        }

        PasswordCommandMutations.ApplyDefaultPassword(user, _passwordHasher, now);
        await GlobalIdentityService.SynchronizePasswordAsync(_context, user.Id, user.Senha, cancellationToken);
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
        var user = await PasswordCommandQueries.GetActiveUserByEmailAsync(_context, email, cancellationToken);

        if (user == null)
        {
            _logger.LogInformation("Solicitacao de reset ignorada porque email nao foi encontrado: {Email}", email);
            return response;
        }

        var token = PasswordResetRules.GenerateToken();
        var tokenEntity = PasswordCommandMutations.CreatePasswordResetToken(user.ClinicaId, user.Id, token, requestIp, now);

        await PasswordCommandMutations.InvalidateActiveTokensAsync(_context, user.Id, now, cancellationToken);
        _context.PasswordResetTokens.Add(tokenEntity);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Token de reset de senha criado para usuario {UserId}", user.Id);

        try
        {
            var dispatchStatus = await _passwordResetNotificationSender.SendAsync(new PasswordResetNotification(
                user.Email,
                user.Nome,
                token,
                tokenEntity.ExpiresAt,
                user.ClinicaId), cancellationToken);

            response.Id = user.Id;
            response.Mode = PasswordResetModes.EmailToken;
            response.DebugToken = _options.ExposeTokenInResponse ? token : null;
            response.Message = dispatchStatus == PasswordResetNotificationDispatchStatus.Sent
                ? "Enviamos um email com o link para redefinir sua senha. Use o link recebido para cadastrar uma nova senha."
                : "Recebemos sua solicitacao. Se o email estiver cadastrado, enviaremos as instrucoes para redefinir a senha.";
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar email de reset de senha para usuario {UserId}", user.Id);
            return await HandleFallbackDefaultPasswordResetAsync(user, now, cancellationToken);
        }
    }

    private async Task<RequestPasswordResetResponse> HandleFallbackDefaultPasswordResetAsync(
        Domain.Models.User user,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await PasswordCommandMutations.InvalidateActiveTokensAsync(_context, user.Id, now, cancellationToken);
        PasswordCommandMutations.ApplyDefaultPassword(user, _passwordHasher, now);
        await _context.SaveChangesAsync(cancellationToken);

        return new RequestPasswordResetResponse
        {
            Id = user.Id,
            PrecisaTrocarSenha = user.PrecisaTrocarSenha,
            Message = "Nao foi possivel enviar o email de redefinicao agora. A senha padrao foi aplicada para voce entrar e trocar a seguir.",
            Mode = PasswordResetModes.DefaultPassword
        };
    }
}
