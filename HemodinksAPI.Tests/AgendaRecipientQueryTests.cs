using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public class AgendaRecipientQueryTests
{
    [Fact]
    public async Task SearchAndProfileAreAppliedBeforePagination_AllRecipientsIsNotLimitedToPage()
    {
        await using var db = TestDbContextFactory.Create();
        for (var i = 0; i < 25; i++) await AgendaSecurityTests.AddUser(db, $"Recipient {i:D2}", Perfil.MedicosId);
        var admin = await AgendaSecurityTests.AddUser(db, "Office", Perfil.AdministradorId);
        await AgendaSecurityTests.AddUser(db, "Patient", Perfil.PacientesId);
        var actor = new CurrentUserContext(999, Perfil.AdministradorId, "Actor");
        var handler = new GetAgendaNotificationRecipientOptionsQueryHandler(db);
        var first = await handler.Handle(new() { CurrentUser = actor, Search = "recipient", Profile = "medical" }, default);
        var second = await handler.Handle(new() { CurrentUser = actor, Search = "recipient", Profile = "medical", Page = 2 }, default);
        Assert.Equal(25, first.TotalUsers);
        Assert.Equal(20, first.Users.Count);
        Assert.Equal(5, second.Users.Count);
        Assert.Empty(first.Users.Select(user => user.Id).Intersect(second.Users.Select(user => user.Id)));
        var office = await handler.Handle(new() { CurrentUser = actor, Profile = "administrative" }, default);
        Assert.Equal(admin.Id, Assert.Single(office.Users).Id);
        var all = EventFeatureRules.ResolveNotificationRecipientUserIds(db, actor,
            new() { NotifyAllAllowedRecipients = true, NotificationMessage = "Message" });
        Assert.Equal(26, all.Count);
        var restricted = await handler.Handle(new() { CurrentUser = new(999, Perfil.MedicosId, "Doctor"), Profile = "medical" }, default);
        Assert.Empty(restricted.Users);
    }

    [Theory]
    [InlineData(0, 20, "all")]
    [InlineData(1, 51, "all")]
    [InlineData(1, 20, "invented")]
    public async Task InvalidQueryIsRejected(int page, int size, string profile)
    {
        await using var db = TestDbContextFactory.Create();
        var handler = new GetAgendaNotificationRecipientOptionsQueryHandler(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new()
        { CurrentUser = new(999, Perfil.AdministradorId, "Actor"), Page = page, PageSize = size, Profile = profile }, default));
    }
}
