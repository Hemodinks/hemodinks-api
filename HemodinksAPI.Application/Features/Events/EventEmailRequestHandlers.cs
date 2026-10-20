using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Events;

public sealed class EventEmailRequestHandlers(IEventEmailFeatureDbContext db, IClinicaContext clinic) :
    IRequestHandler<GetEventEmailNotificationQuery, EventEmailNotificationDto>,
    IRequestHandler<QueueEventEmailNotificationCommand, QueueEventEmailResult>
{
    public async Task<EventEmailNotificationDto> Handle(GetEventEmailNotificationQuery request, CancellationToken ct)
    {
        var actor = await RequireActorAsync(request.CurrentUser, ct);
        var ev = await EventEmailRules.RequireEventAsync(db, request.Id, actor, ct);
        var users = await EventEmailRules.EligibleRecipientsAsync(db, ev.Id, actor, ct);
        var historicalRecipientCount = users.Select(user => EventEmailRules.NormalizeEmail(user.Email))
            .Where(email => email != null).Distinct().Count();
        var creator = await EventEmailRules.EligibleCreatorAsync(db, ev, ct);
        if (request.CopyToCreator && creator != null) users.Add(creator);
        var deliveries = await db.EventEmailDeliveries.AsNoTracking()
            .Where(item => item.ClinicaId == actor.ClinicaId && item.EventId == ev.Id).ToListAsync(ct);
        var recipients = users.Select(user => new { User = user, Email = EventEmailRules.NormalizeEmail(user.Email) })
            .Where(item => item.Email != null).DistinctBy(item => item.Email)
            .Select(item => new EventEmailRecipientDto(item.User.Id, item.User.Nome, item.User.Email,
                deliveries.FirstOrDefault(delivery => delivery.RecipientUserId == item.User.Id
                    || delivery.RecipientEmail == item.Email)?.Status ?? "available")).ToList();
        var historicCount = await db.AgendaNotifications.AsNoTracking()
            .Where(item => item.ClinicaId == actor.ClinicaId && item.EventId == ev.Id)
            .Select(item => item.RecipientUserId).Distinct().CountAsync(ct);
        return new(recipients, recipients.Count, recipients.Count(item => item.Status == "pending"),
            recipients.Count(item => item.Status == "sent"), recipients.Count(item => item.Status == "failed"),
            Math.Max(0, historicCount - historicalRecipientCount), !ev.IsCompleted && recipients.Any(item => item.Status == "available"),
            CreatorCopy(ev, creator, deliveries));
    }

    public async Task<QueueEventEmailResult> Handle(QueueEventEmailNotificationCommand request, CancellationToken ct)
    {
        var actor = await RequireActorAsync(request.CurrentUser, ct);
        var ev = await EventEmailRules.RequireEventAsync(db, request.Id, actor, ct);
        if (ev.IsCompleted) throw new InvalidOperationException("Eventos concluidos nao podem enviar notificacoes por email.");
        var users = await EventEmailRules.EligibleRecipientsAsync(db, ev.Id, actor, ct);
        if (request.CopyToCreator)
        {
            var creator = await EventEmailRules.EligibleCreatorAsync(db, ev, ct);
            if (creator != null) users.Add(creator);
        }
        var existing = await db.EventEmailDeliveries.AsNoTracking()
            .Where(item => item.ClinicaId == actor.ClinicaId && item.EventId == ev.Id).ToListAsync(ct);
        var created = new List<EventEmailDelivery>();
        foreach (var user in users.DistinctBy(user => EventEmailRules.NormalizeEmail(user.Email)))
        {
            var email = EventEmailRules.NormalizeEmail(user.Email);
            if (email == null || existing.Any(item => item.RecipientUserId == user.Id || item.RecipientEmail == email)) continue;
            created.Add(new EventEmailDelivery { ClinicaId = actor.ClinicaId, EventId = ev.Id,
                RecipientUserId = user.Id, RecipientEmail = email, SenderUserId = actor.Id,
                SenderEquipeId = actor.EquipeId, SenderEquipeOperadorId = actor.EquipeOperadorId });
        }
        if (created.Count == 0) return new(0);
        return new(await db.EnqueueEventEmailsAsync(created, ct));
    }

    private static EventEmailCreatorCopyDto CreatorCopy(Event ev, User? creator, List<EventEmailDelivery> deliveries)
    {
        if (!ev.CreatedByUserId.HasValue) return new(null, false, "creator_unknown");
        if (creator == null) return new(null, false, "creator_not_authorized");
        var email = EventEmailRules.NormalizeEmail(creator.Email);
        if (email == null) return new(null, false, "invalid_email");
        var status = deliveries.FirstOrDefault(item => item.RecipientUserId == creator.Id || item.RecipientEmail == email)?.Status ?? "available";
        var recipient = new EventEmailRecipientDto(creator.Id, creator.Nome, creator.Email, status);
        return new(recipient, !ev.IsCompleted && status == "available",
            ev.IsCompleted ? "event_completed" : status == "available" ? null : "already_requested");
    }

    private async Task<HemodinksAPI.Application.Authorization.CurrentUserContext> RequireActorAsync(
        HemodinksAPI.Application.Authorization.CurrentUserContext requested, CancellationToken ct)
    {
        if (clinic.GetRequiredClinicaId() != requested.ClinicaId) throw new UnauthorizedAccessException();
        return await EventEmailRules.RequireActorAsync(db, requested, ct);
    }
}


