using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Features.Events.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class AgendaSecurityTests
{
    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.SuperAdministradorId)]
    [InlineData(Perfil.ControllerId)]
    public async Task Recipients_RejectUnknownUserAndInactiveMembership(int profile)
    {
        await using var db = TestDbContextFactory.Create();
        var active = await AddUser(db, "active", Perfil.AdministradorId);
        var revoked = await AddUser(db, "revoked", Perfil.AdministradorId);
        (await db.UsuariosClinicas.SingleAsync(link => link.UserId == revoked.Id)).Ativo = false;
        await db.SaveChangesAsync();
        var actor = new CurrentUserContext(999, profile, "Actor");
        Assert.Equal(new[] { active.Id }, EventFeatureRules.BuildAllowedNotificationRecipientUserIds(db, actor));
        foreach (var id in new[] { revoked.Id, 123456 })
            Assert.Throws<UnauthorizedAccessException>(() => EventFeatureRules.ResolveNotificationRecipientUserIds(
                db, actor, new EventRequest { NotificationMessage = "Message", NotificationUserIds = [id] }));
    }

    [Theory]
    [InlineData(Perfil.EquipeId)]
    [InlineData(Perfil.PacientesId)]
    [InlineData(Perfil.MedicosId)]
    public async Task Groups_RejectHiddenGroupEvenWhenIdIsKnown(int profile)
    {
        await using var db = TestDbContextFactory.Create();
        var group = new GrupoMedico { Nome = "Restricted" };
        db.GruposMedicos.Add(group);
        await db.SaveChangesAsync();
        Assert.Throws<UnauthorizedAccessException>(() => EventFeatureRules.ResolveNotificationRecipientUserIds(db,
            new CurrentUserContext(999, profile, "Actor"),
            new EventRequest { NotificationMessage = "Message", NotificationGroupIds = [group.Id] }));
    }

    [Fact]
    public async Task Groups_RejectInactiveAndUnknownGroupsForAdministrator()
    {
        await using var db = TestDbContextFactory.Create();
        var group = new GrupoMedico { Nome = "Inactive", Ativo = false };
        db.GruposMedicos.Add(group);
        await db.SaveChangesAsync();
        foreach (var id in new[] { group.Id, 123456 })
            Assert.Throws<UnauthorizedAccessException>(() => EventFeatureRules.ResolveNotificationRecipientUserIds(db,
                new CurrentUserContext(999, Perfil.AdministradorId, "Actor"),
                new EventRequest { NotificationMessage = "Message", NotificationGroupIds = [id] }));
    }

    [Fact]
    public async Task Update_RejectsRecipientBeforeChangingEvent()
    {
        await using var db = TestDbContextFactory.Create();
        var user = await AddUser(db, "owner", Perfil.AdministradorId);
        var ev = new Event { UserId = user.Id, Title = "Original", Start = DateTime.UtcNow, End = DateTime.UtcNow.AddHours(1) };
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        var handler = new EventCommandHandler(db, ClinicaContextFactory.CreateDefaultResolved());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new UpdateEventCommand
        {
            Id = ev.Id, CurrentUser = new(user.Id, user.PerfilId, user.Nome),
            Request = new EventRequest { Title = "Changed", Start = ev.Start, End = ev.End,
                NotificationMessage = "Message", NotificationUserIds = [123456] }
        }, CancellationToken.None));
        Assert.Equal("Original", ev.Title);
        Assert.Empty(db.AgendaNotifications);
    }

    [Fact]
    public async Task Create_RejectsMismatchedAuthenticatedClinic()
    {
        await using var db = TestDbContextFactory.Create();
        var handler = new EventCommandHandler(db, ClinicaContextFactory.CreateDefaultResolved());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new CreateEventCommand
        {
            CurrentUser = new(1, Perfil.SuperAdministradorId, "Actor", ClinicaId: 2), Request = new()
        }, CancellationToken.None));
        Assert.Empty(db.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validation_RejectsMalformedRecipientIds(int id)
    {
        var request = new EventRequest { Title = "Event", Start = DateTime.UtcNow, End = DateTime.UtcNow.AddHours(1),
            NotificationMessage = "Message", NotificationUserIds = [id] };
        Assert.Throws<InvalidOperationException>(() => new CreateEventCommandValidator().Validate(new() { Request = request }));
        Assert.Throws<InvalidOperationException>(() => new UpdateEventCommandValidator().Validate(new() { Id = 1, Request = request }));
    }

    [Fact]
    public void Validation_RejectsNullRecipientCollections()
    {
        Assert.Throws<InvalidOperationException>(() => EventFeatureRules.ValidateNotificationRequest(
            new EventRequest { NotificationUserIds = null! }));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("global")]
    [InlineData("missing-link")]
    public async Task Recipients_RejectInactiveOrUnlinkedAccounts(string state)
    {
        await using var db = TestDbContextFactory.Create();
        var user = await AddUser(db, "recipient", Perfil.ControllerId);
        var link = await db.UsuariosClinicas.Include(item => item.UsuarioGlobal).SingleAsync(item => item.UserId == user.Id);
        if (state == "user") user.Ativo = false;
        if (state == "global") link.UsuarioGlobal.Ativo = false;
        if (state == "missing-link") db.UsuariosClinicas.Remove(link);
        await db.SaveChangesAsync();
        Assert.Empty(EventRecipientScope.AllowedUsers(db, new(999, Perfil.AdministradorId, "Actor")));
        Assert.Throws<UnauthorizedAccessException>(() => EventFeatureRules.ResolveNotificationRecipientUserIds(db,
            new(999, Perfil.AdministradorId, "Actor"), new() { NotificationMessage = "Private", NotificationUserIds = [user.Id] }));
    }

    [Fact]
    public async Task Groups_AllowDoctorOwnGroupAndExcludeRevokedMember()
    {
        await using var db = TestDbContextFactory.Create();
        var actor = await AddUser(db, "actor", Perfil.MedicosId);
        var recipient = await AddUser(db, "recipient", Perfil.MedicosId);
        var revoked = await AddUser(db, "revoked", Perfil.MedicosId);
        (await db.UsuariosClinicas.SingleAsync(link => link.UserId == revoked.Id)).Ativo = false;
        var group = new GrupoMedico { Nome = "Own group", Membros = new List<GrupoMedicoUsuario>
        {
            new() { UserId = actor.Id }, new() { UserId = recipient.Id }, new() { UserId = revoked.Id }
        } };
        db.GruposMedicos.Add(group);
        await db.SaveChangesAsync();
        var recipients = EventFeatureRules.ResolveNotificationRecipientUserIds(db,
            new(actor.Id, actor.PerfilId, actor.Nome), new() { NotificationMessage = "Private", NotificationGroupIds = [group.Id] });
        Assert.Equal(new[] { recipient.Id }, recipients);
    }

    [Fact]
    public async Task Patient_CannotBypassHiddenMedicalRecipientsWithManualPayload()
    {
        await using var db = TestDbContextFactory.Create();
        var patient = await AddUser(db, "patient", Perfil.PacientesId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new EventCommandHandler(db,
            ClinicaContextFactory.CreateDefaultResolved()).Handle(new CreateEventCommand
        {
            CurrentUser = new(patient.Id, patient.PerfilId, patient.Nome),
            Request = new() { Title = "Event", Start = DateTime.UtcNow, End = DateTime.UtcNow.AddHours(1), NotifyMedicalProfile = true }
        }, CancellationToken.None));
        Assert.Empty(db.Events);
    }

    internal static async Task<User> AddUser(AppDbContext db, string name, int profile, int clinicId = 1)
    {
        var user = new User { Nome = name, Email = $"{name}-{Guid.NewGuid():N}@test.local", Telefone = "123",
            Senha = "hash", PerfilId = profile, ClinicaId = clinicId, Ativo = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await GlobalIdentityService.EnsureForUserAsync(db, user, CancellationToken.None);
        return user;
    }
}
