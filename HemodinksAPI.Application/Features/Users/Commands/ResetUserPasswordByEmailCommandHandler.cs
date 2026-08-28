using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Services;
using MediatR;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Application.Features.Users.Commands;

public class ResetUserPasswordByEmailCommandHandler : IRequestHandler<ResetUserPasswordByEmailCommand, RequestPasswordResetResponse>
{
    private readonly IAppDbContext _context;
    private readonly IPasswordResetNotificationSender _passwordResetNotificationSender;
    private readonly PasswordResetOptions _options;
    private readonly ILogger<ResetUserPasswordByEmailCommandHandler> _logger;

    public ResetUserPasswordByEmailCommandHandler(
        IAppDbContext context,
        IPasswordResetNotificationSender passwordResetNotificationSender,
        IOptions<PasswordResetOptions> options,
        ILogger<ResetUserPasswordByEmailCommandHandler> logger)
    {
        _context = context;
        _passwordResetNotificationSender = passwordResetNotificationSender;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RequestPasswordResetResponse> Handle(ResetUserPasswordByEmailCommand request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var email = request.Email.Trim();

        _logger.LogInformation("Solicitacao de reset de senha recebida para {Email}", email);

        return await HandleEmailPasswordResetAsync(email, request.RequestIp, now, cancellationToken);
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
            await _passwordResetNotificationSender.SendAsync(new PasswordResetNotification(
                user.Email,
                user.Nome,
                token,
                tokenEntity.ExpiresAt,
                user.ClinicaId), cancellationToken);

            response.DebugToken = _options.ExposeTokenInResponse ? token : null;
            return response;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Erro ao enviar email de reset de senha para usuario {UserId}", user.Id);
            tokenEntity.UsedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            return PasswordResetRules.CreateRequestResponse(now);
        }
    }
}
