using System.Data.Common;
using System.Diagnostics;
using System.Collections.Concurrent;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class EventReminderBatchSqlServerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(6, false)]
    [InlineData(24, false)]
    [InlineData(100, false)]
    [InlineData(6, true)]
    [InlineData(24, true)]
    [InlineData(100, true)]
    [Trait("Category", "SqlServer")]
    public async Task DueBatch_BoundsDatabaseCommandsAndPreservesDelivery(int size, bool medical)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Setup.Events.AddRange(Enumerable.Range(0, size).Select(i => NewEvent(fixture.Owner, i, medical)));
        fixture.Setup.Events.Add(NewEvent(fixture.ForeignDoctor, -1, true));
        await fixture.Setup.SaveChangesAsync();
        var commands = new Commands();
        await using var db = fixture.Context(1, commands);
        var sender = new Sender();
        var processor = Processor(db, sender);
        var elapsed = new List<double>();
        for (var sample = -1; sample < 5; sample++)
        {
            await fixture.Setup.Events.Where(e => e.ClinicaId == 1).ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.LastReminderSentAt, (DateTime?)null)
                .SetProperty(e => e.NextReminderAt, DateTime.UtcNow.AddMinutes(-10)));
            db.ChangeTracker.Clear();
            commands.Reset();
            sender.Sent.Clear();
            var start = Stopwatch.GetTimestamp();
            Assert.Equal(size, await processor.ProcessDueRemindersAsync(default));
            if (sample >= 0) elapsed.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Assert.Equal(size * (medical ? 2 : 1), sender.Sent.Count);
            Assert.DoesNotContain(sender.Sent, item => item.UserId == fixture.ForeignDoctor.Id);
        }
        elapsed.Sort();
        output.WriteLine($"size={size}; medical={medical}; samples=5; reads={commands.Reads}; writes={commands.Writes}; p95Ms={elapsed[^1]:F2}");
        Assert.Equal(size + 2, commands.Reads); // Candidates + batch load + fresh authorization per event.
        Assert.InRange(commands.Writes, 2, 1 + (size + 41) / 42); // One claim + EF finalization batches.
        Assert.Equal(size, await fixture.Setup.Events.CountAsync(e => e.ClinicaId == 1 && e.LastReminderSentAt != null));
        Assert.True(await fixture.Setup.Events.AnyAsync(e => e.ClinicaId == 2 && e.LastReminderSentAt == null && e.NextReminderAt < DateTime.UtcNow));
        commands.Reset();
        sender.Sent.Clear();
        Assert.Equal(0, await processor.ProcessDueRemindersAsync(default));
        Assert.Empty(sender.Sent);
        Assert.Equal(1, commands.Reads);
        Assert.Equal(0, commands.Writes);
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task BatchLimit_OrdersDueEventsAndLeavesFutureCompletedDisabledAndLeasedEventsAlone()
    {
        await using var fixture = await Fixture.CreateAsync();
        var due = Enumerable.Range(0, 101).Select(i => NewEvent(fixture.Owner, i)).ToArray();
        var future = NewEvent(fixture.Owner, 201); future.NextReminderAt = DateTime.UtcNow.AddHours(1);
        var completed = NewEvent(fixture.Owner, 202); completed.IsCompleted = true;
        var disabled = NewEvent(fixture.Owner, 203); disabled.NotifyUser = false;
        var missing = NewEvent(fixture.Owner, 204); missing.NextReminderAt = null;
        fixture.Setup.Events.AddRange(due.Concat([future, completed, disabled, missing]));
        await fixture.Setup.SaveChangesAsync();
        await using var db = fixture.Context(1);
        var sender = new Sender();
        var processor = Processor(db, sender);
        Assert.Equal(100, await processor.ProcessDueRemindersAsync(default));
        Assert.Equal(due.Take(100).Select(e => "Lembrete: " + e.Title), sender.Sent.Select(item => item.Title));
        Assert.Equal(1, await processor.ProcessDueRemindersAsync(default));
        Assert.Equal("Lembrete: " + due[100].Title, sender.Sent.Last().Title);
        Assert.Equal(0, await processor.ProcessDueRemindersAsync(default));
        Assert.Equal(4, await fixture.Setup.Events.CountAsync(e => e.LastReminderSentAt == null));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("recipient")]
    [InlineData("identity")]
    [InlineData("clinic")]
    [InlineData("team")]
    [InlineData("member")]
    [Trait("Category", "SqlServer")]
    public async Task PlatformWorker_DeduplicatesAndRevalidatesBeforeTheNextEvent(string revocation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = NewEvent(fixture.ForeignDoctor, 0, true); // Owner is also a doctor: send only once.
        var second = NewEvent(fixture.Owner, 1, true);
        second.NotifyUser = false; // Even medical-only delivery requires an eligible owner.
        if (revocation is "team" or "member")
            fixture.Setup.Equipes.Add(new Equipe { ClinicaId = 1, Nome = "Synthetic revocable team", UsuarioLoginId = fixture.Owner.Id,
                Membros = [new() { ClinicaId = 1, UserId = fixture.Doctor.Id }] });
        fixture.Setup.Events.AddRange(first, second);
        await fixture.Setup.SaveChangesAsync();
        await using var db = fixture.Platform();
        var sender = new Sender
        {
            BeforeSend = async _ =>
            {
                switch (revocation)
                {
                    case "owner":
                    case "recipient":
                        var id = revocation == "owner" ? fixture.Owner.Id : fixture.Doctor.Id;
                        await fixture.Setup.UsuariosClinicas.Where(link => link.UserId == id)
                            .ExecuteUpdateAsync(s => s.SetProperty(link => link.Ativo, false));
                        break;
                    case "identity":
                        var globalId = await fixture.Setup.UsuariosClinicas.Where(link => link.UserId == fixture.Owner.Id)
                            .Select(link => link.UsuarioGlobalId).SingleAsync();
                        await fixture.Setup.UsuariosGlobais.Where(global => global.Id == globalId)
                            .ExecuteUpdateAsync(s => s.SetProperty(global => global.Ativo, false));
                        break;
                    case "clinic":
                        await fixture.Setup.Clinicas.Where(clinic => clinic.Id == 1)
                            .ExecuteUpdateAsync(s => s.SetProperty(clinic => clinic.Ativa, false));
                        break;
                    case "team":
                        await fixture.Setup.Equipes.Where(team => team.ClinicaId == 1)
                            .ExecuteUpdateAsync(s => s.SetProperty(team => team.Ativa, false));
                        break;
                    case "member":
                        await fixture.Setup.EquipeMembros.Where(member => member.ClinicaId == 1)
                            .ExecuteUpdateAsync(s => s.SetProperty(member => member.Ativo, false));
                        break;
                }
            }
        };
        Assert.Equal(1, await Processor(db, sender).ProcessDueRemindersAsync(default));
        Assert.Equal(new[] { fixture.ForeignDoctor.Id }, sender.Sent.Select(item => item.UserId));
        Assert.False(await fixture.Setup.Events.AnyAsync(e => e.Id == second.Id && e.LastReminderSentAt != null));
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrentPlatformAndTenant_ClaimOverlappingCandidatesWithoutDuplicateDelivery()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Setup.Events.AddRange(Enumerable.Range(0, 60).Select(i => NewEvent(fixture.Owner, i)));
        fixture.Setup.Events.AddRange(Enumerable.Range(60, 60).Select(i => NewEvent(fixture.ForeignDoctor, i)));
        await fixture.Setup.SaveChangesAsync();
        var barrier = new CandidateBarrier();
        await using var platform = fixture.Platform(barrier);
        await using var tenant = fixture.Context(2, barrier);
        var sender = new ConcurrentSender();
        var results = await Task.WhenAll(Processor(platform, sender).ProcessDueRemindersAsync(default),
            Processor(tenant, sender).ProcessDueRemindersAsync(default));
        Assert.Equal(2, barrier.Arrivals);
        Assert.Equal(120, results.Sum());
        Assert.Equal(120, sender.Sent.Count);
        Assert.Equal(120, sender.Sent.Select(item => item.Title).Distinct().Count());
        var owners = await fixture.Setup.Events.Select(e => new { e.Title, e.UserId }).ToDictionaryAsync(e => "Lembrete: " + e.Title, e => e.UserId);
        Assert.All(sender.Sent, item => Assert.Equal(owners[item.Title], item.UserId));
        Assert.Equal(120, await fixture.Setup.Events.CountAsync(e => e.LastReminderSentAt != null));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("future")]
    [InlineData("disabled")]
    [InlineData("ownership")]
    [Trait("Category", "SqlServer")]
    public async Task Claim_RechecksDueStateAfterCandidateSelection(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var ev = NewEvent(fixture.Owner, 0);
        fixture.Setup.Events.Add(ev);
        await fixture.Setup.SaveChangesAsync();
        var commands = new Commands
        {
            BeforeFirstWrite = async () =>
            {
                var query = fixture.Setup.Events.Where(e => e.Id == ev.Id);
                if (change == "completed") await query.ExecuteUpdateAsync(s => s.SetProperty(e => e.IsCompleted, true));
                else if (change == "future") await query.ExecuteUpdateAsync(s => s.SetProperty(e => e.NextReminderAt, DateTime.UtcNow.AddHours(1)));
                else if (change == "disabled") await query.ExecuteUpdateAsync(s => s.SetProperty(e => e.NotifyUser, false));
                else await query.ExecuteUpdateAsync(s => s.SetProperty(e => e.ClinicaId, 2)
                    .SetProperty(e => e.UserId, fixture.ForeignDoctor.Id));
            }
        };
        await using var db = fixture.Context(1, commands);
        var sender = new Sender();
        Assert.Equal(0, await Processor(db, sender).ProcessDueRemindersAsync(default));
        Assert.True(commands.WriteCallbackInvoked);
        Assert.Empty(sender.Sent);
        var unchanged = await fixture.Setup.Events.AsNoTracking().SingleAsync(e => e.Id == ev.Id);
        Assert.Null(unchanged.LastReminderSentAt);
        if (change == "ownership")
        {
            Assert.Equal(2, unchanged.ClinicaId);
            Assert.Equal(ev.NextReminderAt, unchanged.NextReminderAt);
        }
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task MedicalRecipient_RestrictsAssignedDoctorAndSuppressesInactiveUser()
    {
        await using var fixture = await Fixture.CreateAsync();
        await AgendaSecurityTests.AddUser(fixture.Setup, "unassigned-doctor", Perfil.MedicosId);
        var ev = NewEvent(fixture.Owner, 0, true);
        ev.NotifyUser = false; ev.MedicalUserId = fixture.Doctor.Id;
        fixture.Setup.Events.Add(ev);
        await fixture.Setup.SaveChangesAsync();
        await using var db = fixture.Context(1);
        var sender = new Sender();
        Assert.Equal(1, await Processor(db, sender).ProcessDueRemindersAsync(default));
        Assert.Equal(fixture.Doctor.Id, Assert.Single(sender.Sent).UserId);
        await fixture.Setup.Users.Where(user => user.Id == fixture.Doctor.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(user => user.Ativo, false));
        await fixture.Setup.Events.Where(e => e.Id == ev.Id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.NextReminderAt, DateTime.UtcNow.AddMinutes(-1)).SetProperty(e => e.LastReminderSentAt, (DateTime?)null));
        db.ChangeTracker.Clear(); sender.Sent.Clear();
        Assert.Equal(0, await Processor(db, sender).ProcessDueRemindersAsync(default));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task TeamRecipients_ExcludeInactiveTeamRevokedMembersAndOtherClinics()
    {
        await using var fixture = await Fixture.CreateAsync();
        var revoked = await AgendaSecurityTests.AddUser(fixture.Setup, "revoked-member", Perfil.MedicosId);
        var outsider = await AgendaSecurityTests.AddUser(fixture.Setup, "outsider", Perfil.MedicosId);
        var team = new Equipe { ClinicaId = 1, Nome = "Synthetic reminder team", UsuarioLoginId = fixture.Owner.Id,
            Membros = [new() { ClinicaId = 1, UserId = fixture.Doctor.Id },
                new() { ClinicaId = 1, UserId = revoked.Id, Ativo = false }] };
        fixture.Setup.Equipes.Add(team);
        var ev = NewEvent(fixture.Owner, 0, true); ev.NotifyUser = false;
        fixture.Setup.Events.Add(ev);
        await fixture.Setup.SaveChangesAsync();
        await using var db = fixture.Platform();
        var sender = new Sender();
        Assert.Equal(1, await Processor(db, sender).ProcessDueRemindersAsync(default));
        Assert.Equal(new[] { fixture.Doctor.Id }, sender.Sent.Select(item => item.UserId));
        Assert.DoesNotContain(sender.Sent, item => item.UserId == outsider.Id);
        await fixture.Setup.Equipes.Where(t => t.Id == team.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Ativa, false));
        await fixture.Setup.Events.Where(e => e.Id == ev.Id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.NextReminderAt, DateTime.UtcNow.AddMinutes(-1)).SetProperty(e => e.LastReminderSentAt, (DateTime?)null));
        db.ChangeTracker.Clear(); sender.Sent.Clear();
        Assert.Equal(0, await Processor(db, sender).ProcessDueRemindersAsync(default));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SendFailure_LeavesFiveMinuteRetryAndDoesNotBlockOtherEvents()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = NewEvent(fixture.Owner, 0);
        var second = NewEvent(fixture.Owner, 1);
        fixture.Setup.Events.AddRange(first, second);
        await fixture.Setup.SaveChangesAsync();
        await using var db = fixture.Context(1);
        var sender = new Sender { BeforeSend = item => item.Title == "Lembrete: " + first.Title
            ? Task.FromException(new InvalidOperationException("Synthetic transport failure")) : Task.CompletedTask };
        var before = DateTime.UtcNow;
        Assert.Equal(1, await Processor(db, sender).ProcessDueRemindersAsync(default));
        var failed = await fixture.Setup.Events.AsNoTracking().SingleAsync(e => e.Id == first.Id);
        Assert.Null(failed.LastReminderSentAt);
        Assert.InRange(failed.NextReminderAt!.Value, before.AddMinutes(5), DateTime.UtcNow.AddMinutes(5));
        Assert.Equal("Lembrete: " + second.Title, Assert.Single(sender.Sent).Title);
        await fixture.Setup.Events.Where(e => e.Id == first.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.NextReminderAt, DateTime.UtcNow.AddMinutes(-1)));
        db.ChangeTracker.Clear(); sender.BeforeSend = null; sender.Sent.Clear();
        Assert.Equal(1, await Processor(db, sender).ProcessDueRemindersAsync(default));
        Assert.Equal("Lembrete: " + first.Title, Assert.Single(sender.Sent).Title);
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task UnresolvedTenant_CannotClaimOrSendAnyClinicEvents()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Setup.Events.Add(NewEvent(fixture.Owner, 0));
        await fixture.Setup.SaveChangesAsync();
        await using var db = fixture.Context(null);
        var sender = new Sender();
        Assert.Equal(0, await Processor(db, sender).ProcessDueRemindersAsync(default));
        Assert.Empty(sender.Sent);
        Assert.True(await fixture.Setup.Events.AnyAsync(e => e.NextReminderAt < DateTime.UtcNow && e.LastReminderSentAt == null));
    }

    private static Event NewEvent(User owner, int sequence, bool medical = false) => new()
    {
        ClinicaId = owner.ClinicaId, UserId = owner.Id, Title = $"Synthetic reminder {sequence}",
        Start = DateTime.UtcNow.AddMinutes(10), End = DateTime.UtcNow.AddHours(1), NotifyUser = true,
        NotifyMedicalProfile = medical, NextReminderAt = DateTime.UtcNow.AddMinutes(-10).AddSeconds(sequence)
    };

    private static EventReminderProcessor Processor(AppDbContext db, INotificationService sender) =>
        new(db, sender, NullLogger<EventReminderProcessor>.Instance);

    private sealed class Commands : DbCommandInterceptor
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public Func<Task>? BeforeFirstWrite { get; set; }
        public bool WriteCallbackInvoked { get; private set; }
        public void Reset() { Reads = 0; Writes = 0; }
        private void Capture(DbCommand command)
        {
            if (command.CommandText.Contains("UPDATE [", StringComparison.Ordinal)) Writes++;
            else Reads++;
        }
        private async Task BeforeWrite(DbCommand command)
        {
            if (!WriteCallbackInvoked && BeforeFirstWrite != null && command.CommandText.Contains("UPDATE [", StringComparison.Ordinal))
            { WriteCallbackInvoked = true; await BeforeFirstWrite(); }
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { await BeforeWrite(command); Capture(command); return result; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { await BeforeWrite(command); Writes++; return result; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result)
        { Capture(command); return result; }
    }

    private sealed class CandidateBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public int Arrivals => _arrivals;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("SELECT TOP(", StringComparison.Ordinal)
                && command.CommandText.Contains("ORDER BY [e].[NextReminderAt]", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult();
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }
            return result;
        }
    }

    private sealed class ConcurrentSender : INotificationService
    {
        public ConcurrentBag<Delivery> Sent { get; } = [];
        public Task SendNotificationToUserAsync(int id, string title, string message)
        { Sent.Add(new(id, title)); return Task.CompletedTask; }
        public Task SendNotificationToMedicalProfileAsync(int profile, string title, string message) =>
            throw new InvalidOperationException("Unscoped broadcast is forbidden.");
    }

    private sealed record Delivery(int UserId, string Title);
    private sealed class Sender : INotificationService
    {
        public List<Delivery> Sent { get; } = [];
        public Func<Delivery, Task>? BeforeSend { get; set; }
        public async Task SendNotificationToUserAsync(int id, string title, string message)
        {
            var item = new Delivery(id, title);
            if (BeforeSend != null) await BeforeSend(item);
            Sent.Add(item);
        }
        public Task SendNotificationToMedicalProfileAsync(int profile, string title, string message) =>
            throw new InvalidOperationException("Unscoped broadcast is forbidden.");
    }

    private sealed class Fixture(string connection, PlatformDbContext setup) : IAsyncDisposable
    {
        public PlatformDbContext Setup { get; } = setup;
        public User Owner { get; private set; } = null!;
        public User Doctor { get; private set; } = null!;
        public User ForeignDoctor { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync()
        {
            SqlServerTestConnection.RequireConfigured();
            var connection = SqlServerTestConnection.Create($"HemodinksReminderBatch_{Guid.NewGuid():N}");
            var setup = new PlatformDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
            var fixture = new Fixture(connection, setup);
            try
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Clinicas.Add(new Clinica { Nome = "Synthetic reminder B", Slug = "reminder-b" });
                await setup.SaveChangesAsync();
                fixture.Owner = await AgendaSecurityTests.AddUser(setup, "reminder-owner", Perfil.AdministradorId);
                fixture.Doctor = await AgendaSecurityTests.AddUser(setup, "reminder-doctor", Perfil.MedicosId);
                fixture.ForeignDoctor = await AgendaSecurityTests.AddUser(setup, "reminder-foreign", Perfil.MedicosId, 2);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public AppDbContext Context(int? clinic, DbCommandInterceptor? commands = null)
        {
            var tenant = new ClinicaContext();
            if (clinic.HasValue) tenant.SetCurrent(clinic.Value, clinic == 1 ? Clinica.DefaultSlug : "reminder-b");
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection);
            if (commands != null) options.AddInterceptors(commands);
            return new AppDbContext(options.Options, tenant);
        }
        public PlatformDbContext Platform(DbCommandInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection);
            if (interceptor != null) options.AddInterceptors(interceptor);
            return new PlatformDbContext(options.Options);
        }
        public async ValueTask DisposeAsync()
        {
            try { await Setup.Database.EnsureDeletedAsync(); }
            finally { await Setup.DisposeAsync(); }
        }
    }
}
