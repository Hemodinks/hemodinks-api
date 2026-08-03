using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// Handler para gerar uma senha temporária única para o usuário.
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

            var temporaryPassword = PasswordCommandMutations.ApplyTemporaryPassword(
                user,
                _passwordHasher,
                DateTime.UtcNow);
            await GlobalIdentityService.SynchronizePasswordAsync(_context, user.Id, user.Senha, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return new ResetUserPasswordResponse
            {
                Id = user.Id,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                Message = "Senha temporária gerada. Ela deve ser alterada no próximo acesso.",
                SenhaTemporaria = temporaryPassword
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao resetar senha do usuario: {UserId}", request.UserId);
            throw;
        }
    }
}
