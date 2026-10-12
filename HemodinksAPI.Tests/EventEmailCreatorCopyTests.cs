using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed partial class EventEmailNotificationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatorCopy_OptInPreservesHistoryRemindersAndIndividualStatus(bool copy)
    {
        await using var db = CreateDb();
        var (actor, ev, recipient) = await Seed(db);
        ev.NotifyUser = true;
        ev.ReminderPeriodMinutes = 30;
        ev.NextReminderAt = DateTime.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        var reminder = ev.NextReminderAt;
        var handler = Handler(db);
        var preview = await handler.Handle(new GetEventEmailNotificationQuery(ev.Id, actor, copy), default);
        Assert.Equal(copy ? 2 : 1, preview.RecipientCount);
        Assert.Equal(actor.Id, preview.CreatorCopy!.Recipient!.UserId);
        Assert.True(preview.CreatorCopy.CanSend);
        Assert.Empty(await db.EventEmailDeliveries.ToListAsync());
        Assert.Equal(copy ? 2 : 1, (await handler.Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, copy), default)).QueuedCount);
        Assert.Equal(0, (await handler.Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, copy), default)).QueuedCount);
        if (!copy)
            Assert.Equal(1, (await handler.Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
        var fake = new FakeSender();
        Assert.Equal(2, await Processor(db, fake).ProcessAsync(default));
        Assert.Equal(2, fake.Messages.Count);
        Assert.Contains(fake.Messages, x => x.Email == recipient.Email);
        Assert.Contains(fake.Messages, x => x.Email == preview.CreatorCopy.Recipient.Email);
        Assert.All(fake.Messages, x => Assert.DoesNotContain(recipient.Email, x.Body));
        var status = await handler.Handle(new GetEventEmailNotificationQuery(ev.Id, actor, true), default);
        Assert.Equal(2, status.SentCount);
        Assert.Equal("sent", status.CreatorCopy!.Recipient!.Status);
        Assert.False(status.CreatorCopy.CanSend);
        Assert.Equal(recipient.Id, Assert.Single(await db.AgendaNotifications.ToListAsync()).RecipientUserId);
        Assert.Equal(reminder, ev.NextReminderAt);
        Assert.Null(ev.LastReminderSentAt);
        Assert.True(ev.NotifyUser);
        Assert.Equal(30, ev.ReminderPeriodMinutes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatorCopy_DeduplicatesCreatorAlreadyParticipantOrSharingNormalizedEmail(bool sharedEmail)
    {
        await using var db = CreateDb();
        var (actor, ev, recipient) = await Seed(db);
        var creator = await AgendaSecurityTests.AddUser(db, "creator", Perfil.AdministradorId);
        ev.CreatedByUserId = creator.Id;
        if (sharedEmail) creator.Email = " " + recipient.Email.ToUpperInvariant() + " ";
        else db.AgendaNotifications.Add(Notification(ev, actor.Id, creator.Id));
        await db.SaveChangesAsync();
        var count = sharedEmail ? 1 : 2;
        Assert.Equal(count, (await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
        Assert.Equal(count, await db.EventEmailDeliveries.CountAsync());
        var preview = await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor, true), default);
        Assert.Equal(count, preview.RecipientCount);
        Assert.Equal("pending", preview.CreatorCopy!.Recipient!.Status);
        Assert.Equal(0, (await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("user")]
    [InlineData("membership")]
    [InlineData("global")]
    [InlineData("invalid_email")]
    [InlineData("missing_email")]
    [InlineData("patient")]
    public async Task CreatorCopy_UnavailableCreatorDoesNotBlockParticipants(string state)
    {
        await using var db = CreateDb();
        var (actor, ev, _) = await Seed(db);
        var creator = await AgendaSecurityTests.AddUser(db, "creator", Perfil.AdministradorId);
        ev.CreatedByUserId = state == "legacy" ? null : creator.Id;
        if (state == "user") creator.Ativo = false;
        if (state == "membership") (await db.UsuariosClinicas.SingleAsync(x => x.UserId == creator.Id)).Ativo = false;
        if (state == "global") (await db.UsuariosClinicas.Include(x => x.UsuarioGlobal).SingleAsync(x => x.UserId == creator.Id)).UsuarioGlobal.Ativo = false;
        if (state == "invalid_email") creator.Email = "bad\r\nBcc: attacker@example.com";
        if (state == "missing_email") creator.Email = "";
        if (state == "patient") creator.PerfilId = Perfil.PacientesId;
        await db.SaveChangesAsync();
        var preview = await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor, true), default);
        Assert.False(preview.CreatorCopy!.CanSend);
        Assert.Null(preview.CreatorCopy.Recipient);
        Assert.NotNull(preview.CreatorCopy.UnavailableReason);
        Assert.Equal(1, preview.RecipientCount);
        Assert.Equal(1, (await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
        Assert.DoesNotContain(await db.EventEmailDeliveries.ToListAsync(), x => x.RecipientUserId == creator.Id);
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("global")]
    [InlineData("user")]
    [InlineData("email")]
    [InlineData("patient")]
    public async Task CreatorCopy_WorkerRevalidatesCreatorAfterEnqueue(string state)
    {
        await using var db = CreateDb();
        var (actor, ev, _) = await Seed(db);
        var creator = await AgendaSecurityTests.AddUser(db, "creator", Perfil.AdministradorId);
        ev.CreatedByUserId = creator.Id;
        await db.SaveChangesAsync();
        var originalEmail = creator.Email;
        Assert.Equal(2, (await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
        if (state == "membership") (await db.UsuariosClinicas.SingleAsync(x => x.UserId == creator.Id)).Ativo = false;
        if (state == "global") (await db.UsuariosClinicas.Include(x => x.UsuarioGlobal).SingleAsync(x => x.UserId == creator.Id)).UsuarioGlobal.Ativo = false;
        if (state == "user") creator.Ativo = false;
        if (state == "email") creator.Email = "changed@example.com";
        if (state == "patient") creator.PerfilId = Perfil.PacientesId;
        await db.SaveChangesAsync();
        var fake = new FakeSender();
        Assert.Equal(1, await Processor(db, fake).ProcessAsync(default));
        Assert.DoesNotContain(fake.Messages, x => x.Email == originalEmail || x.Email == "changed@example.com");
        Assert.Equal("failed", (await db.EventEmailDeliveries.SingleAsync(x => x.RecipientUserId == creator.Id)).Status);
    }

    [Fact]
    public async Task CreatorCopy_PartialFailureKeepsIndividualStatusAndDoesNotRequeueSentMessages()
    {
        await using var db = CreateDb();
        var (actor, ev, recipient) = await Seed(db);
        await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default);
        var sender = new SelectivelyFailingSender(recipient.Email);
        var processor = new HemodinksAPI.Infrastructure.Services.EventEmailDeliveryProcessor(db, sender,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HemodinksAPI.Infrastructure.Services.EventEmailDeliveryProcessor>.Instance);
        Assert.Equal(1, await processor.ProcessAsync(default));
        var preview = await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor, true), default);
        Assert.Equal(1, preview.SentCount);
        Assert.Equal(1, preview.PendingCount);
        Assert.Equal("sent", preview.CreatorCopy!.Recipient!.Status);
        Assert.Equal(0, (await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
        Assert.Equal(2, await db.EventEmailDeliveries.CountAsync());
    }

    [Fact]
    public async Task CreatorCopy_DoesNotHideExcludedParticipantsOrBypassCompletedEvent()
    {
        await using var db = CreateDb();
        var (actor, ev, _) = await Seed(db);
        var excluded = await AgendaSecurityTests.AddUser(db, "excluded", Perfil.ControllerId);
        db.AgendaNotifications.Add(Notification(ev, actor.Id, excluded.Id));
        excluded.Ativo = false;
        await db.SaveChangesAsync();
        var preview = await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor, true), default);
        Assert.Equal(2, preview.RecipientCount);
        Assert.Equal(1, preview.ExcludedCount);
        ev.IsCompleted = true;
        await db.SaveChangesAsync();
        preview = await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor, true), default);
        Assert.False(preview.CanSend);
        Assert.False(preview.CreatorCopy!.CanSend);
        Assert.Equal("event_completed", preview.CreatorCopy.UnavailableReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default));
        Assert.Empty(await db.EventEmailDeliveries.ToListAsync());
    }

    [Fact]
    public async Task CreatorCopy_RequesterRevocationSuppressesAllPendingMessages()
    {
        await using var db = CreateDb();
        var (actor, ev, _) = await Seed(db);
        Assert.Equal(2, (await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
        (await db.UsuariosClinicas.SingleAsync(x => x.UserId == actor.Id)).Ativo = false;
        await db.SaveChangesAsync();
        var fake = new FakeSender();
        Assert.Equal(0, await Processor(db, fake).ProcessAsync(default));
        Assert.Empty(fake.Messages);
        Assert.All(await db.EventEmailDeliveries.ToListAsync(), x => Assert.Equal("failed", x.Status));
    }

    [Fact]
    public async Task CreatorCopy_DoesNotGrantNonOwnerPermissionToNotify()
    {
        await using var db = CreateDb();
        var (_, ev, _) = await Seed(db);
        var outsider = await AgendaSecurityTests.AddUser(db, "outsider", Perfil.ControllerId);
        var actor = new HemodinksAPI.Application.Authorization.CurrentUserContext(outsider.Id, outsider.PerfilId, outsider.Nome);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor, true), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default));
        Assert.Empty(await db.EventEmailDeliveries.ToListAsync());
    }

    private sealed class SelectivelyFailingSender(string failingEmail) : IEventEmailSender
    {
        public Task SendAsync(EventEmailMessage message, CancellationToken ct) => message.Email == failingEmail
            ? Task.FromException(new IOException("Simulated transport failure")) : Task.CompletedTask;
    }
}
