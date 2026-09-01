using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

public sealed class EfAuthenticationSessionStore(PlatformDbContext context) : IAuthenticationSessionStore
{
    public Task<UsuarioClinica?> FindActiveMembershipAsync(
        int usuarioGlobalId,
        int userId,
        int clinicaId,
        CancellationToken cancellationToken)
    {
        return ActiveMemberships()
            .FirstOrDefaultAsync(item => item.UsuarioGlobalId == usuarioGlobalId
                && item.UserId == userId
                && item.ClinicaId == clinicaId,
                cancellationToken);
    }

    public Task<AuthenticationSession?> FindByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken)
    {
        return SessionsWithMembership()
            .FirstOrDefaultAsync(item => item.RefreshTokenHash == refreshTokenHash, cancellationToken);
    }

    public Task<AuthenticationSession?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return SessionsWithMembership()
            .FirstOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
    }

    public void Add(AuthenticationSession session) => context.AuthenticationSessions.Add(session);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private IQueryable<UsuarioClinica> ActiveMemberships()
    {
        return context.UsuariosClinicas
            .Include(item => item.UsuarioGlobal)
            .Include(item => item.Clinica)
            .Include(item => item.Perfil)
            .Include(item => item.User).ThenInclude(item => item.Perfil)
            .Include(item => item.User).ThenInclude(item => item.Clinica)
            .Where(item => item.Ativo
                && item.UsuarioGlobal.Ativo
                && item.User.Ativo
                && item.Clinica.Ativa);
    }

    private IQueryable<AuthenticationSession> SessionsWithMembership()
    {
        return context.AuthenticationSessions
            .Include(item => item.UsuarioClinica).ThenInclude(item => item.UsuarioGlobal)
            .Include(item => item.UsuarioClinica).ThenInclude(item => item.Clinica)
            .Include(item => item.UsuarioClinica).ThenInclude(item => item.Perfil)
            .Include(item => item.UsuarioClinica).ThenInclude(item => item.User).ThenInclude(item => item.Perfil)
            .Include(item => item.UsuarioClinica).ThenInclude(item => item.User).ThenInclude(item => item.Clinica);
    }
}
