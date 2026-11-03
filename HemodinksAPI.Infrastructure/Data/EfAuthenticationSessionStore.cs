using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

public sealed class EfAuthenticationSessionStore(PlatformDbContext context) : IAuthenticationSessionStore
{
    public Task<UsuarioClinica?> FindMembershipAsync(int globalId, int membershipId, CancellationToken cancellationToken) =>
        ActiveMemberships().FirstOrDefaultAsync(m => m.Id == membershipId && m.UsuarioGlobalId == globalId, cancellationToken);
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

    public async Task<SessionTeamBinding?> FindTeamBindingAsync(AuthenticationSession session, DateTime now, CancellationToken ct)
    {
        var membership = session.UsuarioClinica;
        var team = await context.Equipes.FirstOrDefaultAsync(t => t.Id == session.EquipeId
            && t.ClinicaId == membership.ClinicaId && t.UsuarioLoginId == membership.UserId
            && t.Ativa && t.VersaoSessao == session.EquipeVersaoSessao, ct);
        if (team == null || membership.User.PerfilId != Perfil.EquipeId) return null;
        EquipeOperador? op = null;
        if (session.EquipeOperadorId.HasValue)
        {
            op = await context.EquipeOperadores.Include(o => o.User).FirstOrDefaultAsync(o => o.Id == session.EquipeOperadorId
                && o.EquipeId == team.Id && o.ClinicaId == membership.ClinicaId && o.Ativo && o.User.Ativo
                && o.User.ClinicaId == membership.ClinicaId && o.VersaoSessao == session.OperadorVersaoSessao
                && (o.BloqueadoAte == null || o.BloqueadoAte <= now)
                && context.EquipeMembros.Any(m => m.EquipeId == team.Id && m.ClinicaId == membership.ClinicaId
                    && m.UserId == o.UserId && m.Ativo), ct);
            if (op == null) return null;
        }
        else if (team.ModoIdentificacao != EquipeModosIdentificacao.Nenhuma) return null;
        if (session.IdentificacaoConfiavel && (op == null || team.ModoIdentificacao != EquipeModosIdentificacao.Pin)) return null;
        return new SessionTeamBinding(team, op);
    }

    public void Add(AuthenticationSession session) => context.AuthenticationSessions.Add(session);

    public async Task<bool> RevokeByIdAsync(Guid sessionId, DateTime revokedAt, CancellationToken cancellationToken)
    {
        var query = context.AuthenticationSessions.Where(s => s.Id == sessionId);
        if (context.Database.IsRelational())
            return await query.ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, revokedAt), cancellationToken) > 0;
        var session = await query.SingleOrDefaultAsync(cancellationToken);
        if (session == null) return false;
        session.RevokedAt = revokedAt;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

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
            try
            {
                await MergeActivityConflictAsync(cancellationToken);
            }
            finally
            {
                // Even a successful activity merge does not authorize the stale aggregate.
                // The caller must read and validate committed security state again.
                context.ChangeTracker.Clear();
            }
            return false;
        }
    }

    private async Task MergeActivityConflictAsync(CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlServer()) return;
        var pending = context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();
        if (pending.Length != 1 || pending[0].State != EntityState.Modified
            || pending[0].Entity is not AuthenticationSession session
            || pending[0].Properties.Count(property => property.IsModified) != 1
            || !pending[0].Property(nameof(AuthenticationSession.LastActivityAt)).IsModified) return;

        var entry = context.Entry(session);
        var contextVersion = entry.Property(s => s.ContextVersion).OriginalValue;
        var securityVersion = entry.Property(s => s.SecurityVersion).OriginalValue;
        var membershipId = entry.Property(s => s.UsuarioClinicaId).OriginalValue;
        var globalId = entry.Property(s => s.UsuarioGlobalId).OriginalValue;
        var refreshHash = entry.Property(s => s.RefreshTokenHash).OriginalValue;
        var clinicId = session.UsuarioClinica.ClinicaId;
        var userId = session.UsuarioClinica.UserId;
        var profileId = session.UsuarioClinica.User.PerfilId;
        var observedAt = session.LastActivityAt;

        // Merge only activity, monotonically, while the security/context snapshot still
        // matches. Never overwrite a refresh, revocation, membership or credential change.
        // UPDATE 0 is also safe: the normal retry rereads and revalidates every condition.
        await context.AuthenticationSessions.Where(s => s.Id == session.Id
            && s.ContextVersion == contextVersion && s.SecurityVersion == securityVersion
            && s.UsuarioClinicaId == membershipId && s.UsuarioGlobalId == globalId
            && s.RefreshTokenHash == refreshHash && s.RevokedAt == null
            && s.UsuarioClinica.Ativo && s.UsuarioClinica.ClinicaId == clinicId
            && s.UsuarioClinica.UserId == userId && s.UsuarioClinica.UsuarioGlobalId == globalId
            && s.UsuarioClinica.UsuarioGlobal.Ativo
            && s.UsuarioClinica.UsuarioGlobal.SecurityVersion == securityVersion
            && s.UsuarioClinica.User.Ativo && s.UsuarioClinica.User.ClinicaId == clinicId
            && s.UsuarioClinica.Clinica.Ativa
            && s.UsuarioClinica.PerfilId == profileId && s.UsuarioClinica.User.PerfilId == profileId
            && s.LastActivityAt < observedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LastActivityAt, observedAt), cancellationToken);
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
