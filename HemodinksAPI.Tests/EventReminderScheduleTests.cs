using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Services;

namespace HemodinksAPI.Tests;

public class EventReminderScheduleTests
{
    [Fact]
    public void CalculateNextReminderAt_WhenNotificationsAreDisabled_ReturnsNull()
    {
        var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var ev = CreateEvent(now.AddDays(3));

        var nextReminderAt = EventReminderSchedule.CalculateNextReminderAt(ev, now);

        Assert.Null(nextReminderAt);
    }

    [Fact]
    public void CalculateNextReminderAt_WhenEventIsCompleted_ReturnsNull()
    {
        var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var ev = CreateEvent(now.AddDays(3), notifyUser: true);
        ev.IsCompleted = true;

        var nextReminderAt = EventReminderSchedule.CalculateNextReminderAt(ev, now);

        Assert.Null(nextReminderAt);
    }

    [Fact]
    public void CalculateNextReminderAt_WhenEventIsOutsideReminderWindow_ReturnsTwoDaysBeforeStart()
    {
        var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var start = now.AddDays(5);
        var ev = CreateEvent(start, notifyUser: true);

        var nextReminderAt = EventReminderSchedule.CalculateNextReminderAt(ev, now);

        Assert.Equal(start.AddDays(-2), nextReminderAt);
    }

    [Fact]
    public void CalculateNextReminderAt_WhenEventIsInsideReminderWindowAndNeverSent_ReturnsNow()
    {
        var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var ev = CreateEvent(now.AddHours(8), notifyUser: true);

        var nextReminderAt = EventReminderSchedule.CalculateNextReminderAt(ev, now);

        Assert.Equal(now, nextReminderAt);
    }

    [Fact]
    public void CalculateNextReminderAt_WhenReminderWasSent_ReturnsConfiguredPeriodAfterLastSent()
    {
        var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var ev = CreateEvent(now.AddHours(8), notifyUser: true);
        ev.ReminderPeriodMinutes = 60;
        ev.LastReminderSentAt = now;

        var nextReminderAt = EventReminderSchedule.CalculateNextReminderAt(ev, now);

        Assert.Equal(now.AddHours(1), nextReminderAt);
    }

    [Fact]
    public void CalculateNextReminderAt_WhenStoredReminderIsAlreadyDue_ReturnsNow()
    {
        var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var ev = CreateEvent(now.AddHours(8), notifyUser: true);
        ev.ReminderPeriodMinutes = 60;
        ev.LastReminderSentAt = now.AddHours(-2);

        var nextReminderAt = EventReminderSchedule.CalculateNextReminderAt(ev, now);

        Assert.Equal(now, nextReminderAt);
    }

    private static Event CreateEvent(DateTime start, bool notifyUser = false, bool notifyMedicalProfile = false)
    {
        return new Event
        {
            UserId = 1,
            Title = "Consulta",
            Start = start,
            End = start.AddHours(1),
            NotifyUser = notifyUser,
            NotifyMedicalProfile = notifyMedicalProfile
        };
    }
}
