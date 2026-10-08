using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

public class ResetUserPasswordByEmailCommandHandler : IRequestHandler<ResetUserPasswordByEmailCommand, RequestPasswordResetResponse>
{
    private readonly IPasswordResetOperationsDbContext _context;
    private readonly IPasswordResetNotificationSender _passwordResetNotificationSender;
    private readonly ILogger<ResetUserPasswordByEmailCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;

    internal ResetUserPasswordByEmailCommandHandler(
        IPasswordResetOperationsDbContext context,
        IPasswordResetNotificationSender passwordResetNotificationSender,
        ILogger<ResetUserPasswordByEmailCommandHandler> logger)
        : this(context, passwordResetNotificationSender, logger, TimeProvider.System)
    {
    }

    public ResetUserPasswordByEmailCommandHandler(
        IPasswordResetOperationsDbContext context,
        IPasswordResetNotificationSender passwordResetNotificationSender,
        ILogger<ResetUserPasswordByEmailCommandHandler> logger,
        TimeProvider timeProvider)
    {
        _context = context;
        _passwordResetNotificationSender = passwordResetNotificationSender;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<RequestPasswordResetResponse> Handle(ResetUserPasswordByEmailCommand request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var email = request.Email.Trim();

        return await HandleEmailPasswordResetAsync(email, request.RequestIp, now, cancellationToken);
    }

    private async Task<RequestPasswordResetResponse> HandleEmailPasswordResetAsync(
        string email,
        string? requestIp,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var response = PasswordResetRules.CreateRequestResponse();
        var user = await PasswordCommandQueries.GetActiveUserByEmailAsync(_context, email, cancellationToken);

        if (user == null)
        {
            return response;
        }

        var token = PasswordResetRules.GenerateToken();
        var tokenEntity = PasswordCommandMutations.CreatePasswordResetToken(user.ClinicaId, user.Id, token, requestIp, now);

        var invalidatedTokens = await PasswordCommandMutations.InvalidateActiveTokensAsync(_context, user.Id, now, cancellationToken);
        _context.PasswordResetTokens.Add(tokenEntity);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A competing recovery already consumed a token. Keep account existence private,
            // and discard this operation's staged changes before any later idempotency save.
            _context.PasswordResetTokens.Remove(tokenEntity);
            foreach (var invalidatedToken in invalidatedTokens)
                await _context.PasswordResetTokens.Attach(invalidatedToken).ReloadAsync(cancellationToken);
            return response;
        }

        _logger.LogInformation("Token de reset de senha criado para usuario {UserId}", user.Id);

        try
        {
            await _passwordResetNotificationSender.SendAsync(new PasswordResetNotification(
                user.Email,
                user.Nome,
                token,
                tokenEntity.ExpiresAt,
                user.ClinicaId), cancellationToken);

            return response;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Erro ao enviar email de reset de senha para usuario {UserId}", user.Id);
            tokenEntity.UsedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            return PasswordResetRules.CreateRequestResponse();
        }
    }
}
