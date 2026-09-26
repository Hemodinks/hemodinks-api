using System.Text.Json;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Features.Events.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class EventScheduleValidationTests
{
    private static EventRequest ValidRequest() => new()
    {
        Title = "Event", Start = new(2026, 9, 26, 23, 30, 0, DateTimeKind.Utc),
        End = new(2026, 9, 27, 0, 30, 0, DateTimeKind.Utc)
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TitleIsRequiredForCreateAndUpdate(string? title)
    {
        var request = ValidRequest();
        request.Title = title;
        AssertInvalid(request, EventScheduleRules.TitleRequired);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void EndMustBeStrictlyAfterStart(int minutes)
    {
        var request = ValidRequest();
        request.End = request.Start.AddMinutes(minutes);
        AssertInvalid(request, EventScheduleRules.EndAfterStart);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultDatesAreRejected(bool start)
    {
        var request = ValidRequest();
        if (start) request.Start = default; else request.End = default;
        AssertInvalid(request, start ? EventScheduleRules.InvalidStart : EventScheduleRules.InvalidEnd);
    }

    [Fact]
    public void CrossDayAndMixedDateTimeKindsUseUtcInstants()
    {
        var request = ValidRequest();
        request.End = request.End.ToLocalTime();
        new CreateEventCommandValidator().Validate(new() { Request = request });
        new UpdateEventCommandValidator().Validate(new() { Id = 1, Request = request });
        var ev = EventFeatureRules.ApplyRequest(new Event(), request, 1, null, true);
        Assert.Equal(TimeSpan.FromHours(1), ev.End - ev.Start);
        Assert.Equal(DateTimeKind.Utc, ev.Start.Kind);
        Assert.Equal(DateTimeKind.Utc, ev.End.Kind);
    }

    [Fact]
    public async Task RelationalRead_RestoresUtcKindAndJsonOffsetWithoutChangingStoredTicks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Events (Id INTEGER PRIMARY KEY, ClinicaId INTEGER, Start TEXT, End TEXT);
            INSERT INTO Events VALUES (1, 1, '2026-09-26 19:00:00', '2026-09-26 20:00:00');
            """);
        var dates = await db.Events.Select(ev => new { ev.Start, ev.End }).SingleAsync();
        Assert.Equal(new DateTime(2026, 9, 26, 19, 0, 0, DateTimeKind.Utc), dates.Start);
        Assert.Equal(DateTimeKind.Utc, dates.Start.Kind);
        Assert.Equal(DateTimeKind.Utc, dates.End.Kind);
        Assert.Contains("19:00:00Z", JsonSerializer.Serialize(dates));
    }

    private static void AssertInvalid(EventRequest request, string message)
    {
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(() =>
            new CreateEventCommandValidator().Validate(new() { Request = request })).Message);
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(() =>
            new UpdateEventCommandValidator().Validate(new() { Id = 1, Request = request })).Message);
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(() =>
            EventFeatureRules.ApplyRequest(new Event(), request, 1, null, true)).Message);
    }
}
