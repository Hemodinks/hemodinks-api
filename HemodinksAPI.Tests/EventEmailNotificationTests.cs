using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed partial class EventEmailNotificationTests
{
    [Fact]
    public async Task PreviewAndQueue_DeduplicateHistoryAndSharedEmails_AndDoubleSubmitIsSafe()
    {
        await using var db = CreateDb();
        var (actor, ev, recipient) = await Seed(db);
        var shared = await AgendaSecurityTests.AddUser(db, "shared", Perfil.ControllerId);
        shared.Email = recipient.Email.ToUpperInvariant();
        db.AgendaNotifications.AddRange(Notification(ev, actor.Id, recipient.Id), Notification(ev, actor.Id, shared.Id));
        await db.SaveChangesAsync();
        var handler = Handler(db);
        var preview = await handler.Handle(new GetEventEmailNotificationQuery(ev.Id, actor), default);
        Assert.Single(preview.Recipients); Assert.Equal(1, preview.ExcludedCount); Assert.True(preview.CanSend);
        Assert.Empty(db.EventEmailDeliveries);
        Assert.Equal(1, (await handler.Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default)).QueuedCount);
        Assert.Equal(0, (await handler.Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default)).QueuedCount);
        var queued = await handler.Handle(new GetEventEmailNotificationQuery(ev.Id, actor), default);
        Assert.Equal(1, queued.PendingCount); Assert.False(queued.CanSend);
        Assert.Single(db.EventEmailDeliveries);
        Assert.Equal(3, await db.AgendaNotifications.CountAsync());
    }

    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.SuperAdministradorId)]
    public async Task AnotherClinic_CannotPreviewOrQueueEvenAsAdministrator(int profile)
    {
        await using var db = CreateDb();
        var (_, ev, _) = await Seed(db);
        db.Clinicas.Add(new Clinica { Id = 2, Nome = "Beta", Slug = "beta", Ativa = true });
        await db.SaveChangesAsync();
        var other = await AgendaSecurityTests.AddUser(db, "beta", profile, 2);
        var clinic = new ClinicaContext(); clinic.SetCurrent(2, "beta");
        var actor = new CurrentUserContext(other.Id, profile, other.Nome, 2);
        var handler = new EventEmailRequestHandlers(db, clinic);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(new GetEventEmailNotificationQuery(ev.Id, actor), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default));
        Assert.Empty(db.EventEmailDeliveries);
    }

    [Theory]
    [InlineData(Perfil.MedicosId)]
    [InlineData(Perfil.ControllerId)]
    [InlineData(Perfil.PacientesId)]
    public async Task NonOwnerAndPatient_AreDenied(int profile)
    {
        await using var db = CreateDb();
        var (_, ev, _) = await Seed(db);
        var user = await AgendaSecurityTests.AddUser(db, "outsider", profile);
        var actor = new CurrentUserContext(user.Id, profile, user.Nome);
        await Assert.ThrowsAnyAsync<Exception>(() => Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default));
        Assert.Empty(db.EventEmailDeliveries);
    }

    [Fact]
    public async Task CompletedEvent_DisablesPreviewAndRejectsQueue()
    {
        await using var db = CreateDb();
        var (actor, ev, _) = await Seed(db);
        ev.IsCompleted = true; await db.SaveChangesAsync();
        Assert.False((await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor), default)).CanSend);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default));
    }

    [Theory]
    [InlineData("recipient")]
    [InlineData("membership")]
    [InlineData("global")]
    [InlineData("sender")]
    [InlineData("clinic")]
    [InlineData("email")]
    [InlineData("completed")]
    [InlineData("patient")]
    public async Task Worker_RevalidatesPersistedPermissionAndSuppressesRevokedDelivery(string state)
    {
        await using var db = CreateDb();
        var (actor, ev, recipient) = await Seed(db);
        await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default);
        if (state == "recipient") recipient.Ativo = false;
        if (state == "membership") (await db.UsuariosClinicas.SingleAsync(link => link.UserId == recipient.Id)).Ativo = false;
        if (state == "global") (await db.UsuariosClinicas.Include(link => link.UsuarioGlobal).SingleAsync(link => link.UserId == recipient.Id)).UsuarioGlobal.Ativo = false;
        if (state == "sender") (await db.Users.SingleAsync(user => user.Id == actor.Id)).Ativo = false;
        if (state == "clinic") (await db.Clinicas.SingleAsync(item => item.Id == 1)).Ativa = false;
        if (state == "email") recipient.Email = "changed@test.local";
        if (state == "completed") ev.IsCompleted = true;
        if (state == "patient") (await db.Users.SingleAsync(user => user.Id == actor.Id)).PerfilId = Perfil.PacientesId;
        await db.SaveChangesAsync();
        var fake = new FakeSender();
        Assert.Equal(0, await Processor(db, fake).ProcessAsync(default));
        Assert.Empty(fake.Messages);
        Assert.Equal("failed", (await db.EventEmailDeliveries.SingleAsync()).Status);
    }

    [Fact]
    public async Task Worker_SendsIndividualPlainMessage_OnlyMarksSentAfterTransportSuccess()
    {
        await using var db = CreateDb();
        var (actor, ev, recipient) = await Seed(db);
        await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default);
        var fake = new FakeSender();
        Assert.Equal(1, await Processor(db, fake).ProcessAsync(default));
        var message = Assert.Single(fake.Messages);
        Assert.Equal(recipient.Email, message.Email);
        Assert.Contains(ev.Title, message.Body); Assert.Contains(ev.Description!, message.Body); Assert.Contains("UTC", message.Body);
        Assert.Equal("sent", (await db.EventEmailDeliveries.SingleAsync()).Status);
        Assert.NotNull((await db.EventEmailDeliveries.SingleAsync()).SentAt);
        Assert.Equal(0, await Processor(db, fake).ProcessAsync(default));
        Assert.Single(fake.Messages);
    }

    [Fact]
    public async Task Worker_RetriesBoundedly_AndDoesNotClaimActiveLease()
    {
        await using var db = CreateDb();
        var (actor, ev, _) = await Seed(db);
        await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default);
        var delivery = await db.EventEmailDeliveries.SingleAsync();
        delivery.LeaseUntil = DateTime.UtcNow.AddMinutes(1); await db.SaveChangesAsync();
        var fake = new FakeSender { Fail = true };
        Assert.Equal(0, await Processor(db, fake).ProcessAsync(default)); Assert.Equal(0, delivery.Attempts);
        delivery.LeaseUntil = null; await db.SaveChangesAsync();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            delivery.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
            Assert.Equal(0, await Processor(db, fake).ProcessAsync(default));
            Assert.Equal(attempt, delivery.Attempts);
            Assert.Equal(attempt < 3 ? "pending" : "failed", delivery.Status);
            Assert.Null(delivery.SentAt);
        }
        Assert.Equal(3, fake.Messages.Count);
    }

    [Fact]
    public async Task DoctorGroupRecipients_AreRevalidatedAfterGroupRevocation()
    {
        await using var db = CreateDb();
        var (admin, ev, recipient) = await Seed(db);
        var doctor = await AgendaSecurityTests.AddUser(db, "doctor", Perfil.MedicosId);
        recipient.PerfilId = Perfil.MedicosId; ev.UserId = doctor.Id;
        var group = new GrupoMedico { Nome = "Doctors", Membros = new List<GrupoMedicoUsuario>
            { new() { UserId = doctor.Id }, new() { UserId = recipient.Id } } };
        db.GruposMedicos.Add(group); await db.SaveChangesAsync();
        var actor = new CurrentUserContext(doctor.Id, doctor.PerfilId, doctor.Nome);
        Assert.Single((await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor), default)).Recipients);
        await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default);
        group.Ativo = false; await db.SaveChangesAsync();
        var fake = new FakeSender();
        Assert.Equal(0, await Processor(db, fake).ProcessAsync(default)); Assert.Empty(fake.Messages);
        var preview = await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor), default);
        Assert.Empty(preview.Recipients); Assert.Equal(1, preview.ExcludedCount);
    }

    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.SuperAdministradorId)]
    public async Task TeamPrivateEvent_DoesNotGrantAdminBypass(int profile)
    {
        await using var db = CreateDb();
        var (actor, ev, _) = await Seed(db, profile);
        var teamLogin = await AgendaSecurityTests.AddUser(db, "team", Perfil.EquipeId);
        db.Equipes.Add(new Equipe { ClinicaId = 1, Nome = "Private", UsuarioLoginId = teamLogin.Id });
        ev.UserId = teamLogin.Id; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default));
    }

    [Theory]
    [InlineData(EquipeModosIdentificacao.Selecao)]
    [InlineData(EquipeModosIdentificacao.Pin)]
    public async Task IdentifiedTeamModes_WorkAndRevokedOperatorIsSuppressed(string mode)
    {
        await using var db = CreateDb();
        var (_, ev, recipient) = await Seed(db);
        var login = await AgendaSecurityTests.AddUser(db, "login", Perfil.EquipeId);
        var team = new Equipe { ClinicaId = 1, Nome = "Team", UsuarioLoginId = login.Id, ModoIdentificacao = mode };
        team.Membros.Add(new EquipeMembro { ClinicaId = 1, UserId = recipient.Id });
        var op = new EquipeOperador { ClinicaId = 1, UserId = recipient.Id, Ativo = true, PrecisaTrocarPin = false };
        team.Operadores.Add(op); db.Equipes.Add(team); ev.UserId = login.Id;
        await db.SaveChangesAsync();
        var actor = new CurrentUserContext(login.Id, login.PerfilId, login.Nome, EquipeId: team.Id,
            EquipeOperadorId: op.Id, IdentificacaoConfiavel: mode == EquipeModosIdentificacao.Pin);
        Assert.True((await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor), default)).CanSend);
        await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default);
        op.Ativo = false; await db.SaveChangesAsync();
        var fake = new FakeSender(); Assert.Equal(0, await Processor(db, fake).ProcessAsync(default)); Assert.Empty(fake.Messages);
    }

    [Fact]
    public async Task AnonymousTeam_CannotQueue()
    {
        await using var db = CreateDb();
        var (_, ev, _) = await Seed(db);
        var login = await AgendaSecurityTests.AddUser(db, "login", Perfil.EquipeId);
        var team = new Equipe { ClinicaId = 1, Nome = "Team", UsuarioLoginId = login.Id, ModoIdentificacao = EquipeModosIdentificacao.Nenhuma };
        db.Equipes.Add(team); ev.UserId = login.Id; await db.SaveChangesAsync();
        var actor = new CurrentUserContext(login.Id, login.PerfilId, login.Nome, EquipeId: team.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor), default));
    }

    [Fact]
    public async Task SmtpWithoutConfiguration_FailsClosedWithoutSending()
    {
        var sender = new SmtpEventEmailSender(Options.Create(new EmailOptions()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(new("fake@test.local", "body"), default));
    }

    private static PlatformDbContext CreateDb()
    {
        var db = new PlatformDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated(); return db;
    }
    private static EventEmailRequestHandlers Handler(AppDbContext db) => new(db, ClinicaContextFactory.CreateDefaultResolved());
    private static EventEmailDeliveryProcessor Processor(PlatformDbContext db, FakeSender fake) =>
        new(db, fake, NullLogger<EventEmailDeliveryProcessor>.Instance);
    private static async Task<(CurrentUserContext, Event, User)> Seed(AppDbContext db, int profile = Perfil.AdministradorId)
    {
        var sender = await AgendaSecurityTests.AddUser(db, "sender", profile);
        var recipient = await AgendaSecurityTests.AddUser(db, "recipient", Perfil.ControllerId);
        var ev = new Event { ClinicaId = 1, UserId = sender.Id, CreatedByUserId = sender.Id, Title = "Title <unsafe>",
            Description = "Description", Start = DateTime.UtcNow.AddDays(1), End = DateTime.UtcNow.AddDays(1).AddHours(1) };
        db.Events.Add(ev); await db.SaveChangesAsync();
        db.AgendaNotifications.Add(Notification(ev, sender.Id, recipient.Id)); await db.SaveChangesAsync();
        return (new(sender.Id, sender.PerfilId, sender.Nome), ev, recipient);
    }
    private static AgendaNotification Notification(Event ev, int senderId, int recipientId) =>
        new() { ClinicaId = ev.ClinicaId, EventId = ev.Id, SenderUserId = senderId, RecipientUserId = recipientId, Title = "Title", Message = "Message" };
    private sealed class FakeSender : IEventEmailSender
    {
        public bool Fail { get; init; }
        public List<EventEmailMessage> Messages { get; } = [];
        public Task SendAsync(EventEmailMessage message, CancellationToken ct)
        {
            Messages.Add(message); if (Fail) throw new IOException("Simulated failure");
            return Task.CompletedTask;
        }
    }
}

