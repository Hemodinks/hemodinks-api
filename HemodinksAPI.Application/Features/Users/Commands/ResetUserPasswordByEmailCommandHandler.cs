using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Services;
using MediatR;

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

        var maskedEmail = HemodinksAPI.Application.Security.SensitiveDataMasking.MaskEmail(email);
        _logger.LogInformation("Solicitacao de reset de senha recebida para {MaskedEmail}", maskedEmail);

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
            _logger.LogInformation(
                "Solicitacao de reset ignorada porque email nao foi encontrado: {MaskedEmail}",
                HemodinksAPI.Application.Security.SensitiveDataMasking.MaskEmail(email));
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
