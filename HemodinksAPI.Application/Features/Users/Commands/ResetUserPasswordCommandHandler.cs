using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// Handler para gerar uma senha temporaria unica para o usuario.
/// </summary>
public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand, ResetUserPasswordResponse>
{
    private readonly IUserFeatureDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ResetUserPasswordCommandHandler> _logger;

    public ResetUserPasswordCommandHandler(
        IUserFeatureDbContext context,
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

            var temporaryPassword = TemporaryPasswordGenerator.Generate();
            PasswordCommandMutations.ApplyTemporaryPassword(user, _passwordHasher, temporaryPassword, DateTime.UtcNow);
            await GlobalIdentityService.SynchronizePasswordAsync(_context, user.Id, user.Senha, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return new ResetUserPasswordResponse
            {
                Id = user.Id,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                Message = "Senha temporaria gerada. O usuario devera troca-la no proximo acesso.",
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
