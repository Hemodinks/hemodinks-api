using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Events;

internal static class EventFeatureRules
{
    public static IQueryable<Event> ApplyScope(IAppDbContext context, IQueryable<Event> query, CurrentUserContext currentUser)
    {
        var teamLoginUserIds = context.Equipes.AsNoTracking()
            .Where(team => team.Ativa)
            .Select(team => team.UsuarioLoginId);

        var currentUserTeamLoginIds = context.EquipeMembros.AsNoTracking()
            .Where(member => member.UserId == currentUser.Id && member.Ativo && member.Equipe.Ativa)
            .Select(member => member.Equipe.UsuarioLoginId);

        if (currentUser.IsEquipe)
        {
            return query.Where(ev => ev.UserId == currentUser.Id);
        }

        if (currentUser.IsAdministrador)
        {
            return query.Where(ev => !teamLoginUserIds.Contains(ev.UserId)
                || currentUserTeamLoginIds.Contains(ev.UserId));
        }

        if (currentUser.IsMedico)
        {
            return query.Where(ev =>
                ev.UserId == currentUser.Id
                || ev.MedicalUserId == currentUser.Id
                || (ev.NotifyMedicalProfile && ev.MedicalUserId == null && !teamLoginUserIds.Contains(ev.UserId))
                || currentUserTeamLoginIds.Contains(ev.UserId));
        }

        return query.Where(ev => ev.UserId == currentUser.Id
            || currentUserTeamLoginIds.Contains(ev.UserId));
    }

    public static void EnsureCanManageEvent(Event ev, CurrentUserContext currentUser)
    {
        if (!currentUser.IsAdministrador && ev.UserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException();
        }
    }

    public static IReadOnlyList<int> ResolveNotificationRecipientUserIds(
        IAppDbContext context,
        CurrentUserContext currentUser,
        EventRequest request)
    {
        var allowedRecipients = BuildAllowedNotificationRecipientUserIds(context, currentUser);
        var recipientIds = new HashSet<int>();

        if (request.NotifyAllAllowedRecipients)
        {
            recipientIds.UnionWith(allowedRecipients);
        }

        foreach (var userId in request.NotificationUserIds.Distinct().Where(id => id > 0))
        {
            if (!allowedRecipients.Contains(userId))
            {
                throw new UnauthorizedAccessException("Um ou mais destinatarios selecionados nao sao permitidos para o perfil atual.");
            }

            recipientIds.Add(userId);
        }

        var allowedGroupMemberIds = BuildAllowedNotificationGroupMemberIds(context, currentUser, request.NotificationGroupIds);
        recipientIds.UnionWith(allowedGroupMemberIds);

        recipientIds.Remove(currentUser.Id);
        return recipientIds.ToList();
    }

    public static HashSet<int> BuildAllowedNotificationRecipientUserIds(IAppDbContext context, CurrentUserContext currentUser)
    {
        if (currentUser.IsEquipe && currentUser.EquipeId.HasValue)
        {
            return context.EquipeMembros.AsNoTracking()
                .Where(member => member.EquipeId == currentUser.EquipeId && member.Ativo && member.User.Ativo)
                .Select(member => member.UserId)
                .ToHashSet();
        }

        if (currentUser.IsAdministrador || currentUser.IsController)
        {
            return context.Users
                .AsNoTracking()
                .Where(user => user.Ativo && user.PerfilId != Perfil.PacientesId && user.Id != currentUser.Id)
                .Select(user => user.Id)
                .ToHashSet();
        }

        if (currentUser.IsMedico)
        {
            return context.Users
                .AsNoTracking()
                .Where(user => user.Ativo
                    && user.Id != currentUser.Id
                    && (Perfil.IsAdministradorOuSuper(user.PerfilId) || user.PerfilId == Perfil.ControllerId))
                .Select(user => user.Id)
                .ToHashSet();
        }

        return [];
    }

    public static IReadOnlyList<int> BuildAllowedNotificationGroupMemberIds(
        IAppDbContext context,
        CurrentUserContext currentUser,
        IEnumerable<int> requestedGroupIds)
    {
        var groupIds = requestedGroupIds.Distinct().Where(id => id > 0).ToList();
        if (!groupIds.Any())
        {
            return [];
        }

        if (currentUser.IsMedico)
        {
            var allowedGroupIds = context.GrupoMedicoUsuarios
                .AsNoTracking()
                .Where(member => member.UserId == currentUser.Id)
                .Select(member => member.GrupoMedicoId)
                .ToHashSet();

            if (groupIds.Any(groupId => !allowedGroupIds.Contains(groupId)))
            {
                throw new UnauthorizedAccessException("Um ou mais grupos selecionados nao fazem parte do escopo do medico.");
            }
        }

        if (!groupIds.Any())
        {
            return [];
        }

        return context.GrupoMedicoUsuarios
            .AsNoTracking()
            .Where(member => groupIds.Contains(member.GrupoMedicoId)
                && member.User.Ativo
                && member.User.PerfilId == Perfil.MedicosId)
            .Select(member => member.UserId)
            .ToList();
    }

    public static void ValidateNotificationRequest(EventRequest request)
    {
        var hasMessage = !string.IsNullOrWhiteSpace(request.NotificationMessage);
        var hasRecipients = request.NotifyAllAllowedRecipients
            || request.NotificationUserIds.Any()
            || request.NotificationGroupIds.Any();

        if (hasRecipients && !hasMessage)
        {
            throw new InvalidOperationException("Informe a mensagem da notificacao.");
        }

        if (!hasRecipients && hasMessage)
        {
            throw new InvalidOperationException("Selecione ao menos um destinatario para enviar a notificacao.");
        }

        if (request.NotificationMessage is { Length: > 500 })
        {
            throw new InvalidOperationException("A mensagem da notificacao deve ter no maximo 500 caracteres.");
        }
    }

    public static Event ApplyRequest(Event ev, EventRequest request, int userId, int? medicalUserId, bool isCreate)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Informe o titulo do evento.");
        }

        var start = ToUtc(request.Start);
        var end = ToUtc(request.End);
        if (end <= start)
        {
            throw new InvalidOperationException("A data final do evento deve ser maior que a data inicial.");
        }

        var reminderPeriodMinutes = request.ReminderPeriodMinutes;
        if (request.NotifyUser || request.NotifyMedicalProfile)
        {
            reminderPeriodMinutes ??= EventReminderSchedule.DefaultReminderPeriodMinutes;
        }

        if (reminderPeriodMinutes.HasValue
            && (reminderPeriodMinutes.Value < EventReminderSchedule.MinimumReminderPeriodMinutes
                || reminderPeriodMinutes.Value > EventReminderSchedule.MaximumReminderPeriodMinutes))
        {
            throw new InvalidOperationException("O periodo de lembrete deve ficar entre 15 minutos e 7 dias.");
        }

        ev.UserId = userId;
        ev.MedicalUserId = medicalUserId;
        ev.Title = title;
        ev.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        ev.Start = start;
        ev.End = end;
        ev.NotifyMedicalProfile = request.NotifyMedicalProfile;
        ev.NotifyUser = request.NotifyUser;
        ev.ReminderPeriodMinutes = reminderPeriodMinutes;
        ev.UpdatedAt = isCreate ? null : DateTime.UtcNow;

        if (request.IsCompleted.HasValue)
        {
            ev.IsCompleted = request.IsCompleted.Value;
            ev.CompletedAt = ev.IsCompleted
                ? ev.CompletedAt ?? DateTime.UtcNow
                : null;
        }

        ev.NextReminderAt = EventReminderSchedule.CalculateNextReminderAt(ev, DateTime.UtcNow);

        return ev;
    }

    public static DateTime ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
    }

    public static EventDto ToDto(Event ev)
    {
        return new EventDto
        {
            Id = ev.Id,
            UserId = ev.UserId,
            UserName = ev.User.Nome,
            MedicalUserId = ev.MedicalUserId,
            MedicalUserName = ev.MedicalUser != null ? ev.MedicalUser.Nome : null,
            Title = ev.Title,
            Description = ev.Description,
            Start = ev.Start,
            End = ev.End,
            NotifyMedicalProfile = ev.NotifyMedicalProfile,
            NotifyUser = ev.NotifyUser,
            ReminderPeriodMinutes = ev.ReminderPeriodMinutes,
            LastReminderSentAt = ev.LastReminderSentAt,
            NextReminderAt = ev.NextReminderAt,
            IsCompleted = ev.IsCompleted,
            CompletedAt = ev.CompletedAt,
            CreatedAt = ev.CreatedAt,
            UpdatedAt = ev.UpdatedAt
        };
    }
}
