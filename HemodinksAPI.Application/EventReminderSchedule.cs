using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Services;

public static class EventReminderSchedule
{
    public const int DefaultReminderPeriodMinutes = 24 * 60;
    public const int MinimumReminderPeriodMinutes = 15;
    public const int MaximumReminderPeriodMinutes = 7 * 24 * 60;

    public static DateTime? CalculateNextReminderAt(Event ev, DateTime now)
    {
        if (ev.IsCompleted || (!ev.NotifyUser && !ev.NotifyMedicalProfile))
        {
            return null;
        }

        var reminderWindowStart = ev.Start.AddDays(-2);
        if (now < reminderWindowStart)
        {
            return reminderWindowStart;
        }

        if (!ev.LastReminderSentAt.HasValue)
        {
            return now;
        }

        var reminderPeriod = TimeSpan.FromMinutes(ev.ReminderPeriodMinutes ?? DefaultReminderPeriodMinutes);
        var nextReminderAt = ev.LastReminderSentAt.Value.Add(reminderPeriod);

        return nextReminderAt <= now ? now : nextReminderAt;
    }
}
