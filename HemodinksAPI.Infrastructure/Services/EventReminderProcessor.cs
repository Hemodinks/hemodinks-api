using System.Data;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.Data.SqlClient;
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
            var candidates = await dueEvents
                .AsNoTracking()
                .OrderBy(e => e.NextReminderAt)
                .ThenBy(e => e.Id)
                .Select(e => new Candidate(e.Id, e.ClinicaId))
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            var claimedIds = await ClaimAsync(candidates, now, cancellationToken);
            if (claimedIds.Count == 0) return 0;
            var loaded = await _context.Events.Where(e => claimedIds.Contains(e.Id))
                    .Include(e => e.User)
                    .Include(e => e.MedicalUser)
                    .ToDictionaryAsync(e => e.Id, cancellationToken);
            // OUTPUT has no ordering guarantee. Keep the original due-time/Id order.
            events = candidates.Where(candidate => loaded.ContainsKey(candidate.Id))
                .Select(candidate => loaded[candidate.Id]).ToList();
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

    private sealed record Candidate(int Id, int ClinicaId);

    private async Task<List<int>> ClaimAsync(IReadOnlyList<Candidate> candidates, DateTime now, CancellationToken ct)
    {
        if (candidates.Count == 0) return [];
        if (_context.Database.IsSqlServer())
        {
            var parameters = new List<object>
            {
                new SqlParameter("@now", SqlDbType.DateTime2) { Value = now },
                new SqlParameter("@lease", SqlDbType.DateTime2) { Value = now.Add(ProcessingLease) }
            };
            var rows = new List<string>();
            for (var i = 0; i < candidates.Count; i++)
            {
                // Only generated parameter names enter SQL text; every value is bound separately.
                rows.Add($"(@id{i}, @clinic{i})");
                parameters.Add(new SqlParameter($"@id{i}", SqlDbType.Int) { Value = candidates[i].Id });
                parameters.Add(new SqlParameter($"@clinic{i}", SqlDbType.Int) { Value = candidates[i].ClinicaId });
            }
            // Candidates come from the tenant-filtered query. Match ownership again at the write boundary.
            // OUTPUT identifies exactly this replica's claims, without relying on a shared lease timestamp.
            var sql = $"""
                UPDATE [e] SET [NextReminderAt] = @lease
                OUTPUT INSERTED.[Id] AS [Value]
                FROM [Events] AS [e]
                INNER JOIN (VALUES {string.Join(", ", rows)}) AS [c]([Id], [ClinicaId])
                    ON [e].[Id] = [c].[Id] AND [e].[ClinicaId] = [c].[ClinicaId]
                WHERE [e].[IsCompleted] = 0 AND [e].[NextReminderAt] IS NOT NULL
                    AND [e].[NextReminderAt] <= @now
                    AND ([e].[NotifyUser] = 1 OR [e].[NotifyMedicalProfile] = 1);
                """;
            return await _context.Database.SqlQueryRaw<int>(sql, parameters.ToArray()).ToListAsync(ct);
        }

        // Retain the existing conditional claim for other relational providers.
        var claimedIds = new List<int>();
        foreach (var candidate in candidates)
        {
            var changed = await _context.Events.Where(e => e.Id == candidate.Id && e.ClinicaId == candidate.ClinicaId
                && !e.IsCompleted && e.NextReminderAt.HasValue && e.NextReminderAt <= now
                && (e.NotifyUser || e.NotifyMedicalProfile))
                .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.NextReminderAt, now.Add(ProcessingLease)), ct);
            if (changed == 1) claimedIds.Add(candidate.Id);
        }
        return claimedIds;
    }

    private async Task<bool> SendReminderAsync(Event ev, DateTime now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var title = $"Lembrete: {ev.Title}";
        var message = BuildReminderMessage(ev, now);
        var sentAny = false;

        // Platform workers have no tenant query filter: constrain every recipient to the event clinic.
        var eligibleUsers = EventRecipientScope.ActiveUsers(_context, ev.ClinicaId);
        // Re-evaluate authorization for EACH event, after any preceding external sends.
        // A batch-wide recipient snapshot would keep revoked memberships eligible until the batch ended.
        var recipientIds = await eligibleUsers
            .Where(user => eligibleUsers.Any(owner => owner.Id == ev.UserId)
                && ((ev.NotifyUser && user.Id == ev.UserId)
                    || (ev.NotifyMedicalProfile && user.PerfilId == Perfil.MedicosId
                        && (!ev.MedicalUserId.HasValue || user.Id == ev.MedicalUserId.Value)
                        && (!_context.Equipes.Any(team => team.ClinicaId == ev.ClinicaId && team.UsuarioLoginId == ev.UserId)
                            || _context.EquipeMembros.Any(member => member.ClinicaId == ev.ClinicaId
                                && member.Ativo && member.UserId == user.Id && member.Equipe.ClinicaId == ev.ClinicaId
                                && member.Equipe.UsuarioLoginId == ev.UserId && member.Equipe.Ativa)))))
            .OrderBy(user => ev.NotifyUser && user.Id == ev.UserId ? 0 : 1).ThenBy(user => user.Id)
            .Select(user => user.Id).ToListAsync(cancellationToken);
        foreach (var recipientId in recipientIds)
        {
            await _notificationService.SendNotificationToUserAsync(recipientId, title, message);
            sentAny = true;
        }

        return sentAny;
    }

    private static string BuildReminderMessage(Event ev, DateTime now)
    {
        var eventStart = ev.Start.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        var responsible = ev.MedicalUser != null ? $" Medico: {ev.MedicalUser.Nome}." : string.Empty;
        var description = string.IsNullOrWhiteSpace(ev.Description) ? string.Empty : $" {ev.Description}";
        var status = ev.IsAllDay && ev.AllDayStartDate.HasValue && ev.AllDayEndDate.HasValue
            ? AllDayEventSchedule.Describe(ev.AllDayStartDate.Value, ev.AllDayEndDate.Value)
                + (ev.Start <= now ? " Evento pendente de conclusao." : string.Empty)
            : ev.Start <= now ? "Evento pendente de conclusao." : $"Evento em {eventStart}.";

        return $"{status}{responsible}{description}";
    }
}
