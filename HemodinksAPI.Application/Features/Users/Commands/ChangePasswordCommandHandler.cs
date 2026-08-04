using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;

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

            PasswordCommandAccess.EnsureCanChangeOwnPassword(request.CurrentUser, request.UserId);
            PasswordCommandRules.ValidatePasswordChangeCandidate(request.NovaSenha);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId && u.Ativo, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            var membership = await GlobalIdentityService.EnsureForUserAsync(_context, user, cancellationToken);
            var globalCredential = membership.Ativo && membership.UsuarioGlobal.Ativo
                ? membership.UsuarioGlobal.Senha
                : null;

            if (globalCredential == null || !_passwordHasher.VerifyPassword(request.SenhaAtual, globalCredential))
            {
                throw new InvalidOperationException("Senha atual invalida");
            }

            if (_passwordHasher.VerifyPassword(request.NovaSenha, globalCredential))
            {
                throw new InvalidOperationException("A nova senha nao pode ser igual a senha atual");
            }

            PasswordCommandMutations.ApplyNewPassword(user, _passwordHasher, request.NovaSenha, requirePasswordChange: false, DateTime.UtcNow);
            await GlobalIdentityService.SynchronizePasswordAsync(_context, user.Id, user.Senha, cancellationToken);
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
