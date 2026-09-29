using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Features.Events.Commands;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public class AgendaAllDayTests
{
    internal static EventRequest Request(string first = "2026-09-26", string last = "2026-09-26", string zone = "America/Sao_Paulo") => new()
    { Title = "Dia inteiro", IsAllDay = true, AllDayStartDate = DateOnly.Parse(first), AllDayEndDate = DateOnly.Parse(last), TimeZoneId = zone };

    [Theory]
    [InlineData("2026-09-26", "2026-09-26", "America/Sao_Paulo", 24)]
    [InlineData("2026-09-26", "2026-09-28", "America/Sao_Paulo", 72)]
    [InlineData("2026-03-08", "2026-03-08", "America/New_York", 23)]
    [InlineData("2026-11-01", "2026-11-01", "America/New_York", 25)]
    [InlineData("2018-11-04", "2018-11-04", "America/Sao_Paulo", 23)]
    public void CivilDatesResolveAcrossDstWithoutAssuming24Hours(string first, string last, string zone, int hours)
    {
        var request = Request(first, last, zone);
        new CreateEventCommandValidator().Validate(new() { Request = request });
        new UpdateEventCommandValidator().Validate(new() { Id = 1, Request = request });
        var ev = EventFeatureRules.ApplyRequest(new Event(), request, 1, null, true);
        Assert.True(ev.IsAllDay);
        Assert.Equal(request.AllDayStartDate, ev.AllDayStartDate);
        Assert.Equal(request.AllDayEndDate, ev.AllDayEndDate);
        Assert.Equal(TimeSpan.FromHours(hours), ev.End - ev.Start);
        Assert.Equal(DateTimeKind.Utc, ev.Start.Kind);
        Assert.Equal(DateTimeKind.Utc, ev.End.Kind);
    }

    [Theory]
    [InlineData("2026-09-28", "2026-09-26", "America/Sao_Paulo")]
    [InlineData("2026-09-26", "2026-09-26", "Invalid/Zone")]
    [InlineData("2026-09-26", "2026-09-26", "")]
    [InlineData("2026-09-26", "9999-12-31", "UTC")]
    public void InvalidPeriodsAreRejected(string first, string last, string zone)
    {
        var request = Request(first, last, zone);
        Assert.Throws<InvalidOperationException>(() => new CreateEventCommandValidator().Validate(new() { Request = request }));
        Assert.Throws<InvalidOperationException>(() => new UpdateEventCommandValidator().Validate(new() { Id = 1, Request = request }));
    }

    [Fact]
    public void DatesAreRequiredAndSwitchingBackToTimedClearsAllDayMetadata()
    {
        var request = Request(); request.AllDayStartDate = null;
        Assert.Throws<InvalidOperationException>(() => EventFeatureRules.ApplyRequest(new Event(), request, 1, null, true));
        var ev = EventFeatureRules.ApplyRequest(new Event(), Request(), 1, null, true);
        var timed = new EventRequest { Title = "Com horário", Start = ev.Start.AddHours(9), End = ev.Start.AddHours(10) };
        EventFeatureRules.ApplyRequest(ev, timed, 1, null, false);
        Assert.False(ev.IsAllDay); Assert.Null(ev.AllDayStartDate); Assert.Null(ev.AllDayEndDate); Assert.Null(ev.TimeZoneId);
        Assert.Equal(timed.Start, ev.Start);
    }

    [Fact]
    public void RemindersKeep48HourWindowAndServerIgnoresClientInstantProjections()
    {
        var request = Request("2090-09-26", "2090-09-26"); request.NotifyUser = true;
        request.Start = DateTime.UtcNow; request.End = request.Start.AddHours(1);
        var ev = EventFeatureRules.ApplyRequest(new Event(), request, 1, null, true);
        Assert.Equal(new DateTime(2090, 9, 26, 3, 0, 0, DateTimeKind.Utc), ev.Start);
        Assert.Equal(ev.Start.AddHours(-48), ev.NextReminderAt);
        Assert.Equal(ev.Start.AddHours(-48), EventReminderSchedule.CalculateNextReminderAt(ev, ev.Start.AddDays(-3)));
    }
}
