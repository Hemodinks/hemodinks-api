using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class AgendaAllDayReminderTests
{
    [Fact]
    public async Task AllDayReminderUsesCivilDatesAndExistingEligibleRecipients()
    {
        await using var db = TestDbContextFactory.Create();
        var owner = await AgendaSecurityTests.AddUser(db, "owner", Perfil.AdministradorId);
        var doctor = await AgendaSecurityTests.AddUser(db, "doctor", Perfil.MedicosId);
        var request = AgendaAllDayTests.Request("2090-09-26", "2090-09-28");
        request.NotifyUser = true; request.NotifyMedicalProfile = true;
        var ev = EventFeatureRules.ApplyRequest(new Event(), request, owner.Id, doctor.Id, true);
        ev.NextReminderAt = DateTime.UtcNow.AddMinutes(-1);
        db.Events.Add(ev); await db.SaveChangesAsync();
        var sender = new Sender();
        var worker = new EventReminderProcessor(db, sender, NullLogger<EventReminderProcessor>.Instance);
        Assert.Equal(1, await worker.ProcessDueRemindersAsync(default));
        Assert.Equal(new[] { owner.Id, doctor.Id }, sender.Ids.Order().ToArray());
        Assert.All(sender.Messages, message => { Assert.Contains("Dia inteiro de 26/09/2090 a 28/09/2090", message); Assert.DoesNotContain("00:00", message); });
        Assert.NotNull(ev.LastReminderSentAt);
    }
    [Fact]
    public async Task AllDayCreationPersistsNotificationsAndDashboardCivilDates()
    {
        await using var db = TestDbContextFactory.Create();
        var owner = await AgendaSecurityTests.AddUser(db, "owner", Perfil.AdministradorId);
        var recipient = await AgendaSecurityTests.AddUser(db, "recipient", Perfil.ControllerId);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var request = AgendaAllDayTests.Request(day.ToString("yyyy-MM-dd"), day.ToString("yyyy-MM-dd"), "UTC");
        request.NotificationMessage = "Aviso de dia inteiro"; request.NotificationUserIds = [recipient.Id];
        var handler = new HemodinksAPI.Application.Features.Events.Commands.EventCommandHandler(db,
            HemodinksAPI.Application.Tenancy.ClinicaContextFactory.CreateDefaultResolved());
        var created = await handler.Handle(new HemodinksAPI.Application.Features.Events.Commands.CreateEventCommand
        { CurrentUser = new(owner.Id, owner.PerfilId, owner.Nome), Request = request }, default);
        var notification = Assert.Single(db.AgendaNotifications);
        Assert.Equal(created.Id, notification.EventId); Assert.Equal(recipient.Id, notification.RecipientUserId);
        Assert.Equal(owner.ClinicaId, notification.ClinicaId);
        var dashboard = await new HemodinksAPI.Application.Features.Dashboard.Queries.GetDashboardNotificationsQueryHandler(db,
            NullLogger<HemodinksAPI.Application.Features.Dashboard.Queries.GetDashboardNotificationsQueryHandler>.Instance)
            .Handle(new() { CurrentUserId = owner.Id, CurrentPerfilId = owner.PerfilId }, default);
        var upcoming = Assert.Single(dashboard, item => item.EventId == created.Id);
        Assert.Equal(day, upcoming.AllDayStartDate); Assert.Equal(day, upcoming.AllDayEndDate);
    }
    private sealed class Sender : INotificationService
    {
        public List<int> Ids { get; } = []; public List<string> Messages { get; } = [];
        public Task SendNotificationToUserAsync(int userId, string title, string message) { Ids.Add(userId); Messages.Add(message); return Task.CompletedTask; }
        public Task SendNotificationToMedicalProfileAsync(int medicoPerfilId, string title, string message) => throw new InvalidOperationException();
    }
}
