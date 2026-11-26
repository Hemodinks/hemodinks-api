using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Sessions;

public interface IAuthenticationSessionStore
{
    Task<UsuarioClinica?> FindMembershipAsync(int globalId, int membershipId, CancellationToken cancellationToken);
    Task<UsuarioClinica?> FindActiveMembershipAsync(
        int usuarioGlobalId,
        int userId,
        int clinicaId,
        CancellationToken cancellationToken);

    Task<AuthenticationSession?> FindByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken);

    Task<AuthenticationSession?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken);

    // #376: sessao cujo refresh token anterior (ja rotacionado) tem este hash.
    Task<AuthenticationSession?> FindByPreviousRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken);

    // #389: sessao e instante de rotacao de qualquer refresh token ja rotacionado (familia da sessao).
    Task<RotatedRefreshToken?> FindRotatedRefreshTokenAsync(string refreshTokenHash, CancellationToken cancellationToken);

    // #389: registra o hash rotacionado; persiste no mesmo SaveChanges da rotacao.
    void AddRotatedRefreshToken(Guid sessionId, string refreshTokenHash, DateTime rotatedAt);

    // #389: apaga hashes rotacionados antes do corte (nenhuma sessao viva tem historico mais antigo que o prazo absoluto).
    Task<int> PurgeRotatedRefreshTokensAsync(DateTime rotatedBefore, CancellationToken cancellationToken);

    Task<SessionTeamBinding?> FindTeamBindingAsync(AuthenticationSession session, DateTime now, CancellationToken cancellationToken);
    void Add(AuthenticationSession session);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
    Task<bool> RevokeByIdAsync(Guid sessionId, DateTime revokedAt, CancellationToken cancellationToken);
}
