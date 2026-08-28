using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Utils;
using MediatR;

namespace HemodinksAPI.Application.Features.Users.Commands;

public class ConfirmPasswordResetCommandHandler : IRequestHandler<ConfirmPasswordResetCommand, ResetUserPasswordResponse>
{
    private readonly IUserFeatureDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ConfirmPasswordResetCommandHandler> _logger;

    public ConfirmPasswordResetCommandHandler(
        IUserFeatureDbContext context,
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

        var now = DateTime.UtcNow;
        var resetToken = await PasswordCommandQueries.GetValidResetTokenAsync(_context, request.Token, now, cancellationToken);
        if (resetToken == null)
        {
            throw new InvalidOperationException("Token de reset invalido ou expirado");
        }

        PasswordCommandMutations.ApplyNewPassword(resetToken.User, _passwordHasher, request.NovaSenha, requirePasswordChange: false, now);
        await GlobalIdentityService.SynchronizePasswordAsync(_context, resetToken.UserId, resetToken.User.Senha, cancellationToken);
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
