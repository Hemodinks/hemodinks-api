using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class EventReminderProcessorConcurrencyTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task ProcessDueReminders_ClaimsEventBeforeSendingAcrossReplicas()
    {
        var databaseName = $"HemodinksReminderLease_{Guid.NewGuid():N}";
        var connectionString = SqlServerTestConnection.Create(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var firstTenant = ClinicaContextFactory.CreateDefaultResolved();
        var secondTenant = ClinicaContextFactory.CreateDefaultResolved();

        await using var firstDb = new AppDbContext(options, firstTenant);
        await using var secondDb = new AppDbContext(options, secondTenant);
        try
        {
            await firstDb.Database.MigrateAsync();
            var user = new User
            {
                ClinicaId = Clinica.DefaultId,
                Nome = "Usuario lembrete concorrente",
                Email = $"reminder-{databaseName}@test.local",
                Telefone = "+5581999000999",
                Senha = "hash",
                PerfilId = Perfil.MedicosId,
                Ativo = true,
                PrecisaTrocarSenha = false
            };
            firstDb.Users.Add(user);
            await firstDb.SaveChangesAsync();
            firstDb.Events.Add(new Event
            {
                ClinicaId = Clinica.DefaultId,
                UserId = user.Id,
                Title = "Lembrete concorrente",
                Start = DateTime.UtcNow.AddMinutes(10),
                End = DateTime.UtcNow.AddMinutes(40),
                NotifyUser = true,
                NextReminderAt = DateTime.UtcNow.AddMinutes(-1)
            });
            await firstDb.SaveChangesAsync();

            var blockingNotifications = new BlockingNotificationService();
            var firstProcessor = new EventReminderProcessor(
                firstDb,
                blockingNotifications,
                NullLogger<EventReminderProcessor>.Instance);
            var secondNotifications = new CountingNotificationService();
            var secondProcessor = new EventReminderProcessor(
                secondDb,
                secondNotifications,
                NullLogger<EventReminderProcessor>.Instance);

            var firstRun = firstProcessor.ProcessDueRemindersAsync(CancellationToken.None);
            await blockingNotifications.WaitUntilSendingAsync();
            try
            {
                Assert.Equal(0, await secondProcessor.ProcessDueRemindersAsync(CancellationToken.None));
                Assert.Equal(0, secondNotifications.SendCount);
            }
            finally
            {
                blockingNotifications.Release();
            }

            Assert.Equal(1, await firstRun);
            Assert.Equal(1, blockingNotifications.SendCount);
        }
        finally
        {
            await firstDb.Database.EnsureDeletedAsync();
        }
    }

    private sealed class CountingNotificationService : INotificationService
    {
        public int SendCount { get; private set; }

        public Task SendNotificationToUserAsync(int userId, string title, string message)
        {
            SendCount++;
            return Task.CompletedTask;
        }

        public Task SendNotificationToMedicalProfileAsync(int medicoPerfilId, string title, string message)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingNotificationService : INotificationService
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SendCount { get; private set; }

        public async Task SendNotificationToUserAsync(int userId, string title, string message)
        {
            SendCount++;
            _entered.TrySetResult();
            await _release.Task;
        }

        public Task SendNotificationToMedicalProfileAsync(int medicoPerfilId, string title, string message) =>
            SendNotificationToUserAsync(medicoPerfilId, title, message);

        public Task WaitUntilSendingAsync() => _entered.Task;

        public void Release() => _release.TrySetResult();
    }
}
