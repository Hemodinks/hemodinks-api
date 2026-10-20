using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed partial class EventEmailNotificationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatorCopy_DoctorLosingEventVisibilityCannotReceiveCopy(bool afterEnqueue)
    {
        await using var db = CreateDb();
        var (actor, ev, _) = await Seed(db);
        var creator = await AgendaSecurityTests.AddUser(db, "creator-doctor", Perfil.MedicosId);
        ev.CreatedByUserId = creator.Id;
        ev.UserId = creator.Id;
        await db.SaveChangesAsync();
        if (afterEnqueue)
            Assert.Equal(2, (await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
        ev.UserId = actor.Id;
        await db.SaveChangesAsync();
        var preview = await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor, true), default);
        Assert.False(preview.CreatorCopy!.CanSend);
        Assert.Null(preview.CreatorCopy.Recipient);
        if (afterEnqueue)
        {
            var fake = new FakeSender();
            Assert.Equal(1, await Processor(db, fake).ProcessAsync(default));
            Assert.DoesNotContain(fake.Messages, x => x.Email == creator.Email);
            Assert.Equal("failed", (await db.EventEmailDeliveries.SingleAsync(x => x.RecipientUserId == creator.Id)).Status);
        }
        else
            Assert.Equal(1, (await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatorCopy_AdministratorOutsidePrivateTeamCannotReceiveCopy(bool afterEnqueue)
    {
        await using var db = CreateDb();
        var (creator, ev, recipient) = await Seed(db);
        var creatorEmail = await db.Users.Where(x => x.Id == creator.Id).Select(x => x.Email).SingleAsync();
        var login = await AgendaSecurityTests.AddUser(db, "team-login", Perfil.EquipeId);
        var team = new Equipe { ClinicaId = 1, Nome = "Private team", UsuarioLoginId = login.Id,
            ModoIdentificacao = EquipeModosIdentificacao.Selecao };
        team.Membros.Add(new EquipeMembro { ClinicaId = 1, UserId = recipient.Id });
        var creatorMembership = new EquipeMembro { ClinicaId = 1, UserId = creator.Id, Ativo = afterEnqueue };
        team.Membros.Add(creatorMembership);
        var op = new EquipeOperador { ClinicaId = 1, UserId = recipient.Id, Ativo = true, PrecisaTrocarPin = false };
        team.Operadores.Add(op);
        db.Equipes.Add(team);
        ev.UserId = login.Id;
        await db.SaveChangesAsync();
        var actor = new CurrentUserContext(login.Id, login.PerfilId, login.Nome, EquipeId: team.Id, EquipeOperadorId: op.Id);
        if (afterEnqueue)
            Assert.Equal(2, (await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
        creatorMembership.Ativo = false;
        await db.SaveChangesAsync();
        var preview = await Handler(db).Handle(new GetEventEmailNotificationQuery(ev.Id, actor, true), default);
        Assert.False(preview.CreatorCopy!.CanSend);
        if (afterEnqueue)
        {
            var fake = new FakeSender();
            Assert.Equal(1, await Processor(db, fake).ProcessAsync(default));
            Assert.DoesNotContain(fake.Messages, x => x.Email == creatorEmail);
            Assert.Equal("failed", (await db.EventEmailDeliveries.SingleAsync(x => x.RecipientUserId == creator.Id)).Status);
        }
        else
            Assert.Equal(1, (await Handler(db).Handle(new QueueEventEmailNotificationCommand(ev.Id, actor, true), default)).QueuedCount);
    }
}
