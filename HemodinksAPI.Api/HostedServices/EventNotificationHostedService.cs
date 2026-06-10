using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.HostedServices;

public class EventNotificationHostedService : BackgroundService
{
    private const int DefaultReminderPeriodMinutes = 24 * 60;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventNotificationHostedService> _logger;

    public EventNotificationHostedService(IServiceProvider serviceProvider, ILogger<EventNotificationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventNotificationHostedService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifier = scope.ServiceProvider.GetRequiredService<Services.INotificationService>();

                var now = DateTime.UtcNow;

                var events = await db.Events
                    .Include(e => e.User)
                    .Include(e => e.MedicalUser)
                    .Where(e => !e.IsCompleted
                        && e.End >= now
                        && e.Start <= now.AddDays(2)
                        && (e.NotifyUser || e.NotifyMedicalProfile))
                    .ToListAsync(stoppingToken);

                foreach (var ev in events)
                {
                    if (!ShouldSendReminder(ev, now))
                    {
                        continue;
                    }

                    var title = $"Lembrete: {ev.Title}";
                    var message = BuildReminderMessage(ev);
                    var sentAny = false;

                    if (ev.NotifyUser)
                    {
                        await notifier.SendNotificationToUserAsync(ev.UserId, title, message);
                        sentAny = true;
                    }

                    if (ev.NotifyMedicalProfile)
                    {
                        if (ev.MedicalUserId.HasValue)
                        {
                            await notifier.SendNotificationToUserAsync(ev.MedicalUserId.Value, title, message);
                        }
                        else
                        {
                            await notifier.SendNotificationToMedicalProfileAsync(Perfil.MedicosId, title, message);
                        }

                        sentAny = true;
                    }

                    if (sentAny)
                    {
                        ev.LastReminderSentAt = now;
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no processamento das notificacoes de eventos");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private static bool ShouldSendReminder(Event ev, DateTime now)
    {
        if (now < ev.Start.AddDays(-2))
        {
            return false;
        }

        if (ev.LastReminderSentAt == null)
        {
            return true;
        }

        var reminderPeriod = TimeSpan.FromMinutes(ev.ReminderPeriodMinutes ?? DefaultReminderPeriodMinutes);
        return now - ev.LastReminderSentAt.Value >= reminderPeriod;
    }

    private static string BuildReminderMessage(Event ev)
    {
        var eventStart = ev.Start.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        var responsible = ev.MedicalUser != null ? $" Medico: {ev.MedicalUser.Nome}." : string.Empty;
        var description = string.IsNullOrWhiteSpace(ev.Description) ? string.Empty : $" {ev.Description}";

        return $"Evento em {eventStart}.{responsible}{description}";
    }
}
