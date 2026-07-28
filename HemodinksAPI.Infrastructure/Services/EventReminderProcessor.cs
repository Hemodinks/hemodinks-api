using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Services;

public class EventReminderProcessor : IEventReminderProcessor
{
    private const int BatchSize = 100;
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(30);

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
        var dueEvents = _context.Events
            .Where(e => !e.IsCompleted
                && e.NextReminderAt.HasValue
                && e.NextReminderAt <= now
                && (e.NotifyUser || e.NotifyMedicalProfile));

        List<Event> events;
        if (_context.Database.IsRelational())
        {
            var candidateIds = await dueEvents
                .AsNoTracking()
                .OrderBy(e => e.NextReminderAt)
                .ThenBy(e => e.Id)
                .Select(e => e.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            events = [];
            foreach (var eventId in candidateIds)
            {
                var claimed = await _context.Events
                    .Where(e => e.Id == eventId
                        && !e.IsCompleted
                        && e.NextReminderAt.HasValue
                        && e.NextReminderAt <= now
                        && (e.NotifyUser || e.NotifyMedicalProfile))
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            e => e.NextReminderAt,
                            now.Add(ProcessingLease)),
                        cancellationToken);
                if (claimed == 0)
                {
                    continue;
                }

                events.Add(await _context.Events
                    .Include(e => e.User)
                    .Include(e => e.MedicalUser)
                    .SingleAsync(e => e.Id == eventId, cancellationToken));
            }
        }
        else
        {
            events = await dueEvents
            .Include(e => e.User)
            .Include(e => e.MedicalUser)
            .OrderBy(e => e.NextReminderAt)
            .ThenBy(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        }

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
