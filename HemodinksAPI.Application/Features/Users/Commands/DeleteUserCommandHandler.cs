using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using HemodinksAPI.Domain.Exceptions;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// Handler para excluir usuario.
/// </summary>
public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    private readonly IUserFeatureDbContext _context;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IUserFeatureDbContext context,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Excluindo usuario: {UserId}", request.Id);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

            if (user == null)
            {
                throw new ResourceNotFoundException("Usuario nao encontrado");
            }

            if (request.CurrentUser != null)
            {
                if (!request.CurrentUser.IsAdministrador)
                {
                    throw new UnauthorizedAccessException("Sem permissao para excluir usuario");
                }

                // Inclui quem é SuperAdministrador em outra clínica (identidade global).
                if (await SuperAdministradorProtection.IsProtectedFromAsync(_context, user, request.CurrentUser, cancellationToken))
                {
                    throw new UnauthorizedAccessException("Somente outro SuperAdministrador pode excluir este cadastro");
                }
            }

            user.Ativo = false;
            user.DataAtualizacao = DateTime.UtcNow;
            await GlobalIdentityService.SynchronizeUserAsync(_context, user, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir usuario: {UserId}", request.Id);
            throw;
        }
    }
}
