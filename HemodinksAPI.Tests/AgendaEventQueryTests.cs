using HemodinksAPI.Application.Features.Events.Queries;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public class AgendaEventQueryTests
{
    private sealed class Reminders : IEventReminderProcessor
    {
        public int Calls { get; private set; }
        public Task<int> ProcessDueRemindersAsync(CancellationToken cancellationToken) { Calls++; return Task.FromResult(0); }
    }
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    public async Task InvalidFiltersFailBeforeProcessingReminders(int owner, bool invalidRange)
    {
        await using var db = TestDbContextFactory.Create();
        var reminders = new Reminders();
        var handler = new GetEventsQueryHandler(db, reminders);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new GetEventsQuery
        {
            CurrentUser = new(1, Perfil.AdministradorId, "Actor"), UserId = owner,
            From = invalidRange ? DateTime.UtcNow.AddDays(1) : null, To = invalidRange ? DateTime.UtcNow : null
        }, default));
        Assert.Equal(0, reminders.Calls);
    }
    [Fact]
    public async Task SearchAndOwnerNeverExpandRoleVisibility()
    {
        await using var db = TestDbContextFactory.Create();
        var owner = await AgendaSecurityTests.AddUser(db, "Owner", Perfil.AdministradorId);
        var doctor = await AgendaSecurityTests.AddUser(db, "Doctor", Perfil.MedicosId);
        db.Events.Add(new Event { UserId = owner.Id, Title = "Private meeting", Start = DateTime.UtcNow, End = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var handler = new GetEventsQueryHandler(db, new Reminders());
        var hidden = await handler.Handle(new GetEventsQuery { CurrentUser = new(doctor.Id, doctor.PerfilId, doctor.Nome), Search = "private", UserId = owner.Id }, default);
        Assert.Empty(hidden);
    }
}
