using HemodinksAPI.Api.Authorization;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Services;

namespace HemodinksAPI.Api.Features.Events;

internal static class EventFeatureRules
{
    public static IQueryable<Event> ApplyScope(IQueryable<Event> query, CurrentUserContext currentUser)
    {
        if (currentUser.IsAdministrador)
        {
            return query;
        }

        if (currentUser.IsMedico)
        {
            return query.Where(ev =>
                ev.UserId == currentUser.Id
                || ev.MedicalUserId == currentUser.Id
                || (ev.NotifyMedicalProfile && ev.MedicalUserId == null));
        }

        return query.Where(ev => ev.UserId == currentUser.Id);
    }

    public static void EnsureCanManageEvent(Event ev, CurrentUserContext currentUser)
    {
        if (!currentUser.IsAdministrador && ev.UserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException();
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
