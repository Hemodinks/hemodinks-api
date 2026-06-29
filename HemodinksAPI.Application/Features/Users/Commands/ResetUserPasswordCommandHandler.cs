using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

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

            PasswordCommandMutations.ApplyDefaultPassword(user, _passwordHasher, DateTime.UtcNow);
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
