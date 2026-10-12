using System.Net.Mail;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Events;

public static class EventEmailRules
{
    public static async Task<CurrentUserContext> RequireActorAsync(
        IEventEmailFeatureDbContext db, CurrentUserContext requested, CancellationToken ct)
    {
        var user = await EventRecipientScope.ActiveUsers(db, requested.ClinicaId)
            .SingleOrDefaultAsync(item => item.Id == requested.Id, ct)
            ?? throw new UnauthorizedAccessException();
        if (user.PerfilId == Perfil.PacientesId || user.PerfilId != requested.PerfilId)
            throw new UnauthorizedAccessException();
        if (user.PerfilId == Perfil.EquipeId)
        {
            if (!requested.EquipeId.HasValue || !requested.EquipeOperadorId.HasValue)
                throw new UnauthorizedAccessException();
            var activeUsers = EventRecipientScope.ActiveUsers(db, requested.ClinicaId).Select(item => item.Id);
            if (!await db.EquipeOperadores.AsNoTracking().AnyAsync(op =>
                    op.ClinicaId == requested.ClinicaId && op.Id == requested.EquipeOperadorId
                    && op.EquipeId == requested.EquipeId && op.Ativo
                    && (op.Equipe.ModoIdentificacao != EquipeModosIdentificacao.Pin || !op.PrecisaTrocarPin)
                    && op.Equipe.Ativa && op.Equipe.ModoIdentificacao != EquipeModosIdentificacao.Nenhuma
                    && op.Equipe.UsuarioLoginId == user.Id
                    && activeUsers.Contains(op.UserId)
                    && db.EquipeMembros.Any(member => member.ClinicaId == requested.ClinicaId
                        && member.EquipeId == op.EquipeId && member.UserId == op.UserId && member.Ativo), ct))
                throw new UnauthorizedAccessException();
        }
        else if (requested.EquipeId.HasValue || requested.EquipeOperadorId.HasValue)
            throw new UnauthorizedAccessException();
        return requested with { Nome = user.Nome };
    }

    public static async Task<Event> RequireEventAsync(
        IEventEmailFeatureDbContext db, int eventId, CurrentUserContext actor, CancellationToken ct)
    {
        var ev = await EventFeatureRules.ApplyScope(db, db.Events.AsNoTracking(), actor)
            .SingleOrDefaultAsync(item => item.Id == eventId && item.ClinicaId == actor.ClinicaId, ct)
            ?? throw new KeyNotFoundException();
        EventFeatureRules.EnsureCanManageEvent(ev, actor);
        return ev;
    }

    public static async Task<List<User>> EligibleRecipientsAsync(
        IEventEmailFeatureDbContext db, int eventId, CurrentUserContext actor, CancellationToken ct)
    {
        var historicalIds = db.AgendaNotifications.AsNoTracking()
            .Where(item => item.ClinicaId == actor.ClinicaId && item.EventId == eventId)
            .Select(item => item.RecipientUserId);
        var allowedIds = EventRecipientScope.AllowedUsers(db, actor).Select(item => item.Id);
        var allowedGroups = EventRecipientScope.AllowedGroups(db, actor).Select(item => item.Id);
        var groupMemberIds = db.GrupoMedicoUsuarios.AsNoTracking().Where(item =>
            item.ClinicaId == actor.ClinicaId && allowedGroups.Contains(item.GrupoMedicoId)).Select(item => item.UserId);
        return await EventRecipientScope.ActiveUsers(db, actor.ClinicaId).Where(user =>
            user.Id != actor.Id && historicalIds.Contains(user.Id)
            && (allowedIds.Contains(user.Id) || (user.PerfilId == Perfil.MedicosId && groupMemberIds.Contains(user.Id))))
            .OrderBy(user => user.Id).ToListAsync(ct);
    }

    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || email.Contains('\r') || email.Contains('\n'))
            return null;
        var value = email.Trim();
        return MailAddress.TryCreate(value, out var address) && address.Address == value
            ? value.ToLowerInvariant() : null;
    }
}



