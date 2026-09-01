using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Sessions;

public interface IAuthenticationSessionStore
{
    Task<UsuarioClinica?> FindActiveMembershipAsync(
        int usuarioGlobalId,
        int userId,
        int clinicaId,
        CancellationToken cancellationToken);

    Task<AuthenticationSession?> FindByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken);

    Task<AuthenticationSession?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken);

    void Add(AuthenticationSession session);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}
