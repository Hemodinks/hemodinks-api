using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class EventEmailConcurrencyTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrentRequestsAndWorkers_QueueAndClaimEachRecipientOnlyOnce()
    {
        var databaseName = $"HemodinksEventEmailTest_{Guid.NewGuid():N}";
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        await using var seed = new PlatformDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var actorUser = await AgendaSecurityTests.AddUser(seed, "actor", Perfil.AdministradorId);
            var recipient = await AgendaSecurityTests.AddUser(seed, "recipient", Perfil.ControllerId);
            var ev = new Event { ClinicaId = 1, UserId = actorUser.Id, Title = "Concurrent event",
                Start = DateTime.UtcNow.AddDays(1), End = DateTime.UtcNow.AddDays(1).AddHours(1) };
            seed.Events.Add(ev); await seed.SaveChangesAsync();
            seed.AgendaNotifications.Add(new() { ClinicaId = 1, EventId = ev.Id, SenderUserId = actorUser.Id,
                RecipientUserId = recipient.Id, Title = "Title", Message = "Message" });
            await seed.SaveChangesAsync();
            var actor = new CurrentUserContext(actorUser.Id, actorUser.PerfilId, actorUser.Nome);
            await using var queueFirst = new PlatformDbContext(options);
            await using var queueSecond = new PlatformDbContext(options);
            var command = new QueueEventEmailNotificationCommand(ev.Id, actor);
            var results = await Task.WhenAll(
                new EventEmailRequestHandlers(queueFirst, ClinicaContextFactory.CreateDefaultResolved()).Handle(command, default),
                new EventEmailRequestHandlers(queueSecond, ClinicaContextFactory.CreateDefaultResolved()).Handle(command, default));
            Assert.Equal(1, results.Sum(item => item.QueuedCount));
            Assert.Single(await seed.EventEmailDeliveries.AsNoTracking().ToListAsync());

            await using var workerFirstDb = new PlatformDbContext(options);
            await using var workerSecondDb = new PlatformDbContext(options);
            var blocking = new BlockingSender();
            var secondSender = new CountingSender();
            var firstRun = new EventEmailDeliveryProcessor(workerFirstDb, blocking,
                NullLogger<EventEmailDeliveryProcessor>.Instance).ProcessAsync(default);
            await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            try
            {
                Assert.Equal(0, await new EventEmailDeliveryProcessor(workerSecondDb, secondSender,
                    NullLogger<EventEmailDeliveryProcessor>.Instance).ProcessAsync(default));
                Assert.Equal(0, secondSender.Count);
            }
            finally { blocking.Release.TrySetResult(); }
            Assert.Equal(1, await firstRun);
            var delivery = Assert.Single(await seed.EventEmailDeliveries.AsNoTracking().ToListAsync());
            Assert.Equal("sent", delivery.Status); Assert.Equal(1, delivery.Attempts);
            Assert.Equal(1, blocking.Count);
        }
        finally
        {
            // Options target only the randomly generated LocalDB test database above.
            await seed.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task RelationalOutbox_CannotLinkRecipientFromAnotherClinic()
    {
        var databaseName = $"HemodinksEventEmailIsolationTest_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True").Options;
        await using var db = new PlatformDbContext(options);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var actor = await AgendaSecurityTests.AddUser(db, "actor", Perfil.SuperAdministradorId);
            var beta = new Clinica { Id = 0, Nome = "Beta", Slug = "beta", Ativa = true }; db.Clinicas.Add(beta); await db.SaveChangesAsync();
            var other = await AgendaSecurityTests.AddUser(db, "beta", Perfil.ControllerId, beta.Id);
            var ev = new Event { ClinicaId = 1, UserId = actor.Id, Title = "Event",
                Start = DateTime.UtcNow, End = DateTime.UtcNow.AddHours(1) };
            db.Events.Add(ev); await db.SaveChangesAsync();
            db.EventEmailDeliveries.Add(new() { ClinicaId = 1, EventId = ev.Id, SenderUserId = actor.Id,
                RecipientUserId = other.Id, RecipientEmail = other.Email });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private class CountingSender : IEventEmailSender
    {
        public int Count { get; protected set; }
        public virtual Task SendAsync(EventEmailMessage message, CancellationToken ct)
        { Count++; return Task.CompletedTask; }
    }
    private sealed class BlockingSender : CountingSender
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task SendAsync(EventEmailMessage message, CancellationToken ct)
        { Count++; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
    }
}


