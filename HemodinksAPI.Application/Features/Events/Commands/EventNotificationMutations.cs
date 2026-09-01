using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Events.Commands;

internal static class EventNotificationMutations
{
    public static void AddAgendaNotifications(
        IEventFeatureDbContext context,
        Event ev,
        CurrentUserContext currentUser,
        EventRequest request,
        int clinicaId)
    {
        var recipientUserIds = EventFeatureRules.ResolveNotificationRecipientUserIds(context, currentUser, request);
        if (recipientUserIds.Count == 0)
        {
            return;
        }

        var title = ev.Title.Trim();
        var message = string.IsNullOrWhiteSpace(request.NotificationMessage)
            ? (ev.Description?.Trim() ?? title)
            : request.NotificationMessage.Trim();

        foreach (var recipientUserId in recipientUserIds)
        {
            context.AgendaNotifications.Add(new AgendaNotification
            {
                ClinicaId = clinicaId,
                Event = ev,
                SenderUserId = currentUser.Id,
                RecipientUserId = recipientUserId,
                Title = title,
                Message = message,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
