using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Services;

public sealed class EventEmailDeliveryProcessor(
    PlatformDbContext db, IEventEmailSender sender, ILogger<EventEmailDeliveryProcessor> logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private const int MaximumAttempts = 3;

    public async Task<int> ProcessAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var candidates = await db.EventEmailDeliveries.AsNoTracking()
            .Where(item => item.Status == "pending" && item.NextAttemptAt <= now
                && (item.LeaseUntil == null || item.LeaseUntil <= now))
            .OrderBy(item => item.NextAttemptAt).ThenBy(item => item.Id)
            .Select(item => new { item.Id, item.ClinicaId }).Take(100).ToListAsync(ct);
        var sentCount = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var token = Guid.NewGuid();
            var claimNow = DateTime.UtcNow;
            var claim = db.EventEmailDeliveries.Where(item => item.Id == candidate.Id
                && item.ClinicaId == candidate.ClinicaId && item.Status == "pending"
                && item.NextAttemptAt <= claimNow && (item.LeaseUntil == null || item.LeaseUntil <= claimNow));
            EventEmailDelivery delivery;
            if (db.Database.IsRelational())
            {
                var changed = await claim.ExecuteUpdateAsync(setters =>
                    setters.SetProperty(item => item.LeaseId, token)
                        .SetProperty(item => item.LeaseUntil, claimNow.Add(LeaseDuration))
                        .SetProperty(item => item.Attempts, item => item.Attempts + 1), ct);
                if (changed != 1) continue;
                delivery = await db.EventEmailDeliveries.AsNoTracking().SingleAsync(item =>
                    item.Id == candidate.Id && item.ClinicaId == candidate.ClinicaId && item.LeaseId == token, ct);
            }
            else
            {
                delivery = await claim.SingleAsync(ct);
                delivery.LeaseId = token; delivery.LeaseUntil = claimNow.Add(LeaseDuration); delivery.Attempts++;
                await db.SaveChangesAsync(ct);
            }
            var status = "failed";
            DateTime? sentAt = null;
            try
            {
                if (delivery.Attempts <= MaximumAttempts)
                {
                    var notification = await BuildMessageAsync(delivery, ct);
                    if (notification != null)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(60));
                        await sender.SendAsync(notification, timeout.Token);
                        status = "sent"; sentAt = DateTime.UtcNow; sentCount++;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (UnauthorizedAccessException) { /* Revoked authorization permanently suppresses delivery. */ }
            catch (KeyNotFoundException) { /* The event is no longer available to this sender. */ }
            catch (Exception exception)
            {
                // Provider exception messages can contain recipient addresses and event content.
                logger.LogWarning("Falha na entrega de email {DeliveryId}: {ErrorType}", delivery.Id, exception.GetType().Name);
                status = delivery.Attempts < MaximumAttempts ? "pending" : "failed";
            }
            var nextAttempt = DateTime.UtcNow.AddMinutes(delivery.Attempts * 2);
            if (db.Database.IsRelational())
            {
                await db.EventEmailDeliveries.Where(item => item.Id == delivery.Id
                    && item.ClinicaId == delivery.ClinicaId && item.LeaseId == token && item.Status == "pending")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, status)
                        .SetProperty(item => item.SentAt, sentAt).SetProperty(item => item.NextAttemptAt, nextAttempt)
                        .SetProperty(item => item.LeaseUntil, (DateTime?)null)
                        .SetProperty(item => item.LeaseId, (Guid?)null), ct);
            }
            else
            {
                delivery.Status = status; delivery.SentAt = sentAt; delivery.NextAttemptAt = nextAttempt;
                delivery.LeaseUntil = null; delivery.LeaseId = null; await db.SaveChangesAsync(ct);
            }
        }
        return sentCount;
    }

    private async Task<EventEmailMessage?> BuildMessageAsync(EventEmailDelivery delivery, CancellationToken ct)
    {
        var actorUser = await EventRecipientScope.ActiveUsers(db, delivery.ClinicaId)
            .SingleOrDefaultAsync(item => item.Id == delivery.SenderUserId, ct);
        if (actorUser == null) return null;
        var actor = await EventEmailRules.RequireActorAsync(db,
            new CurrentUserContext(actorUser.Id, actorUser.PerfilId, actorUser.Nome, delivery.ClinicaId,
                EquipeId: delivery.SenderEquipeId, EquipeOperadorId: delivery.SenderEquipeOperadorId), ct);
        var ev = await EventEmailRules.RequireEventAsync(db, delivery.EventId, actor, ct);
        if (ev.IsCompleted) return null;
        var users = await EventEmailRules.EligibleRecipientsAsync(db, ev.Id, actor, ct);
        var user = users.SingleOrDefault(item => item.Id == delivery.RecipientUserId);
        if (user == null || EventEmailRules.NormalizeEmail(user.Email) != delivery.RecipientEmail) return null;
        var dates = ev.IsAllDay && ev.AllDayStartDate.HasValue && ev.AllDayEndDate.HasValue
            ? $"Datas: {ev.AllDayStartDate:dd/MM/yyyy} a {ev.AllDayEndDate:dd/MM/yyyy} (dia inteiro)."
            : $"Inicio: {ev.Start:dd/MM/yyyy HH:mm} UTC\nFim: {ev.End:dd/MM/yyyy HH:mm} UTC";
        // One individual, plain-text message; no recipient list or user-supplied mail headers.
        return new(user.Email, $"Voce foi incluido em um evento da agenda HemoDinks.\n\nTitulo: {ev.Title}\n{dates}\n\n{ev.Description}");
    }
}


