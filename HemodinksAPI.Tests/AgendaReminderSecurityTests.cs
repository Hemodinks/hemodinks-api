using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class AgendaReminderSecurityTests
{
    [Fact]
    public async Task PlatformWorker_SendsOnlyToActiveMembersOfEachEventClinic()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, ClinicaContextFactory.CreatePlatform());
        await db.Database.EnsureCreatedAsync();
        db.Clinicas.Add(new Clinica { Id = 2, Nome = "Beta", Slug = "beta" });
        await db.SaveChangesAsync();
        var owner = await AgendaSecurityTests.AddUser(db, "owner", Perfil.AdministradorId);
        var doctor = await AgendaSecurityTests.AddUser(db, "doctor", Perfil.MedicosId);
        var otherDoctor = await AgendaSecurityTests.AddUser(db, "other", Perfil.MedicosId, 2);
        var revoked = await AgendaSecurityTests.AddUser(db, "revoked", Perfil.MedicosId);
        (await db.UsuariosClinicas.SingleAsync(link => link.UserId == revoked.Id)).Ativo = false;
        db.Events.Add(new Event { ClinicaId = 1, UserId = owner.Id, Title = "Alpha private",
            Start = DateTime.UtcNow.AddHours(1), End = DateTime.UtcNow.AddHours(2),
            NotifyMedicalProfile = true, NextReminderAt = DateTime.UtcNow.AddMinutes(-1) });
        db.Events.Add(new Event { ClinicaId = 2, UserId = otherDoctor.Id, Title = "Beta private",
            Start = DateTime.UtcNow.AddHours(1), End = DateTime.UtcNow.AddHours(2),
            NotifyMedicalProfile = true, NotifyUser = true, NextReminderAt = DateTime.UtcNow.AddMinutes(-1) });
        await db.SaveChangesAsync();
        var sender = new RecordingSender();
        var processor = new EventReminderProcessor(db, sender, NullLogger<EventReminderProcessor>.Instance);
        Assert.Equal(2, await processor.ProcessDueRemindersAsync(CancellationToken.None));
        Assert.Equal(new[] { (doctor.Id, "Lembrete: Alpha private"), (otherDoctor.Id, "Lembrete: Beta private") }, sender.Sent);
    }

    [Fact]
    public async Task TeamReminder_ExcludesDoctorsOutsideTeamAndRevokedMembers()
    {
        await using var db = TestDbContextFactory.Create();
        var owner = await AgendaSecurityTests.AddUser(db, "team", Perfil.EquipeId);
        var member = await AgendaSecurityTests.AddUser(db, "member", Perfil.MedicosId);
        var revoked = await AgendaSecurityTests.AddUser(db, "revoked", Perfil.MedicosId);
        await AgendaSecurityTests.AddUser(db, "outsider", Perfil.MedicosId);
        db.Equipes.Add(new Equipe { ClinicaId = 1, Nome = "Team", UsuarioLoginId = owner.Id, Membros = new List<EquipeMembro>
        {
            new() { ClinicaId = 1, UserId = member.Id }, new() { ClinicaId = 1, UserId = revoked.Id, Ativo = false }
        } });
        db.Events.Add(new Event { UserId = owner.Id, Title = "Team private",
            Start = DateTime.UtcNow.AddHours(1), End = DateTime.UtcNow.AddHours(2),
            NotifyMedicalProfile = true, NextReminderAt = DateTime.UtcNow.AddMinutes(-1) });
        await db.SaveChangesAsync();
        var sender = new RecordingSender();
        var processor = new EventReminderProcessor(db, sender, NullLogger<EventReminderProcessor>.Instance);
        Assert.Equal(1, await processor.ProcessDueRemindersAsync(CancellationToken.None));
        Assert.Equal(new[] { (member.Id, "Lembrete: Team private") }, sender.Sent);
    }

    private sealed class RecordingSender : INotificationService
    {
        public List<(int, string)> Sent { get; } = [];
        public Task SendNotificationToUserAsync(int userId, string title, string message)
        {
            Sent.Add((userId, title));
            return Task.CompletedTask;
        }
        public Task SendNotificationToMedicalProfileAsync(int medicoPerfilId, string title, string message)
            => throw new InvalidOperationException("Unscoped profile broadcasts are forbidden.");
    }
}
