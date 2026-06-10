using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Services;

public class EventReminderProcessor : IEventReminderProcessor
{
    private const int BatchSize = 100;

    private readonly AppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ILogger<EventReminderProcessor> _logger;

    public EventReminderProcessor(
        AppDbContext context,
        INotificationService notificationService,
        ILogger<EventReminderProcessor> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<int> ProcessDueRemindersAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var events = await _context.Events
            .Include(e => e.User)
            .Include(e => e.MedicalUser)
            .Where(e => !e.IsCompleted
                && e.NextReminderAt.HasValue
                && e.NextReminderAt <= now
                && (e.NotifyUser || e.NotifyMedicalProfile))
            .OrderBy(e => e.NextReminderAt)
            .ThenBy(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var processedCount = 0;

        foreach (var ev in events)
        {
            try
            {
                if (!await SendReminderAsync(ev, now, cancellationToken))
                {
                    ev.NextReminderAt = EventReminderSchedule.CalculateNextReminderAt(ev, now);
                    continue;
                }

                ev.LastReminderSentAt = now;
                ev.NextReminderAt = EventReminderSchedule.CalculateNextReminderAt(ev, now);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao enviar lembrete do evento {EventId}", ev.Id);
                ev.NextReminderAt = now.AddMinutes(5);
            }
        }

        if (events.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return processedCount;
    }

    private async Task<bool> SendReminderAsync(Event ev, DateTime now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var title = $"Lembrete: {ev.Title}";
        var message = BuildReminderMessage(ev, now);
        var sentAny = false;

        if (ev.NotifyUser)
        {
            await _notificationService.SendNotificationToUserAsync(ev.UserId, title, message);
            sentAny = true;
        }

        if (ev.NotifyMedicalProfile)
        {
            if (ev.MedicalUserId.HasValue)
            {
                await _notificationService.SendNotificationToUserAsync(ev.MedicalUserId.Value, title, message);
            }
            else
            {
                await _notificationService.SendNotificationToMedicalProfileAsync(Perfil.MedicosId, title, message);
            }

            sentAny = true;
        }

        return sentAny;
    }

    private static string BuildReminderMessage(Event ev, DateTime now)
    {
        var eventStart = ev.Start.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        var responsible = ev.MedicalUser != null ? $" Medico: {ev.MedicalUser.Nome}." : string.Empty;
        var description = string.IsNullOrWhiteSpace(ev.Description) ? string.Empty : $" {ev.Description}";
        var status = ev.Start <= now ? "Evento pendente de conclusao." : $"Evento em {eventStart}.";

        return $"{status}{responsible}{description}";
    }
}
