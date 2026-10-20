using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task EventEmailCopy_ExplicitOptInQueuesPersistedCreatorWithoutChangingNotifications()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        int recipientId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(1, Clinica.DefaultSlug);
            recipientId = (await AgendaSecurityTests.AddUser(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                "CopyRecipient", Perfil.ControllerId)).Id;
        }
        var start = DateTime.UtcNow.AddDays(1);
        using var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/events/", new
        {
            title = "Email with creator copy", start, end = start.AddHours(1),
            notificationMessage = "Original message", notificationUserIds = new[] { recipientId },
            notifyUser = true, reminderPeriodMinutes = 30
        }));
        var id = created.RootElement.GetProperty("id").GetInt32();
        var creatorId = created.RootElement.GetProperty("userId").GetInt32();
        using var normal = await ReadJsonAsync(await client.GetAsync($"/api/events/{id}/email-notification"));
        Assert.Equal(1, normal.RootElement.GetProperty("recipientCount").GetInt32());
        using var opted = await ReadJsonAsync(await client.GetAsync($"/api/events/{id}/email-notification?copyToCreator=true"));
        Assert.Equal(2, opted.RootElement.GetProperty("recipientCount").GetInt32());
        var response = await client.PostAsJsonAsync($"/api/events/{id}/email-notification", new
        {
            copyToCreator = true, creatorUserId = recipientId, creatorEmail = "attacker@example.com", clinicaId = 999
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var queued = await ReadJsonAsync(response);
        Assert.Equal(2, queued.RootElement.GetProperty("queuedCount").GetInt32());
        using var duplicate = await ReadJsonAsync(await client.PostAsJsonAsync($"/api/events/{id}/email-notification",
            new { copyToCreator = true }));
        Assert.Equal(0, duplicate.RootElement.GetProperty("queuedCount").GetInt32());
        using var status = await ReadJsonAsync(await client.GetAsync($"/api/events/{id}/email-notification"));
        Assert.Equal("pending", status.RootElement.GetProperty("creatorCopy").GetProperty("recipient").GetProperty("status").GetString());
        using var verify = factory.Services.CreateScope();
        verify.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(1, Clinica.DefaultSlug);
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var deliveries = await db.EventEmailDeliveries.Where(x => x.EventId == id).ToListAsync();
        Assert.Equal(new[] { creatorId, recipientId }.Order().ToArray(), deliveries.Select(x => x.RecipientUserId).Order().ToArray());
        Assert.All(deliveries, x => Assert.Equal(1, x.ClinicaId));
        Assert.DoesNotContain(deliveries, x => x.RecipientEmail == "attacker@example.com");
        var notifications = await db.AgendaNotifications.Where(x => x.EventId == id).ToListAsync();
        Assert.Equal(recipientId, Assert.Single(notifications).RecipientUserId);
        Assert.Equal("Original message", notifications[0].Message);
        var persisted = await db.Events.SingleAsync(x => x.Id == id);
        Assert.True(persisted.NotifyUser);
        Assert.Equal(30, persisted.ReminderPeriodMinutes);
        Assert.Null(persisted.LastReminderSentAt);
    }

    [Fact]
    public async Task EventEmailCopy_OmittedBodyPreservesOldContract_AndOptInWorksWithoutParticipants()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        var start = DateTime.UtcNow.AddDays(1);
        using var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/events/", new
            { title = "Creator only", start, end = start.AddHours(1) }));
        var id = created.RootElement.GetProperty("id").GetInt32();
        using var normal = await ReadJsonAsync(await client.PostAsync($"/api/events/{id}/email-notification", null));
        Assert.Equal(0, normal.RootElement.GetProperty("queuedCount").GetInt32());
        using var opted = await ReadJsonAsync(await client.PostAsJsonAsync($"/api/events/{id}/email-notification", new { copyToCreator = true }));
        Assert.Equal(1, opted.RootElement.GetProperty("queuedCount").GetInt32());
    }

    [Fact]
    public async Task EventEmailCopy_CreatorComesFromAuthenticationAndSurvivesOwnerReassignment()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        int creatorId, ownerId, nextOwnerId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(1, Clinica.DefaultSlug);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            creatorId = await db.Users.Where(x => x.Email == "gmarcone@gmail.com").Select(x => x.Id).SingleAsync();
            ownerId = (await AgendaSecurityTests.AddUser(db, "Owner", Perfil.ControllerId)).Id;
            nextOwnerId = (await AgendaSecurityTests.AddUser(db, "NextOwner", Perfil.ControllerId)).Id;
        }
        var start = DateTime.UtcNow.AddDays(1);
        using var forged = await client.PostAsJsonAsync("/api/events/", new
            { title = "Assigned event", start, end = start.AddHours(1), userId = ownerId, createdByUserId = nextOwnerId });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var creation = await client.PostAsJsonAsync("/api/events/", new
            { title = "Assigned event", start, end = start.AddHours(1), userId = ownerId });
        Assert.True(creation.StatusCode == HttpStatusCode.Created, await creation.Content.ReadAsStringAsync());
        using var created = await ReadJsonAsync(creation);
        var id = created.RootElement.GetProperty("id").GetInt32();
        using var forgedUpdate = await client.PutAsJsonAsync($"/api/events/{id}", new
            { title = "Reassigned event", start, end = start.AddHours(1), userId = nextOwnerId, createdByUserId = ownerId });
        Assert.Equal(HttpStatusCode.BadRequest, forgedUpdate.StatusCode);
        var updated = await client.PutAsJsonAsync($"/api/events/{id}", new
            { title = "Reassigned event", start, end = start.AddHours(1), userId = nextOwnerId });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var queued = await ReadJsonAsync(await client.PostAsJsonAsync($"/api/events/{id}/email-notification", new { copyToCreator = true }));
        Assert.Equal(1, queued.RootElement.GetProperty("queuedCount").GetInt32());
        using var verify = factory.Services.CreateScope();
        verify.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(1, Clinica.DefaultSlug);
        var check = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var ev = await check.Events.SingleAsync(x => x.Id == id);
        Assert.Equal(nextOwnerId, ev.UserId);
        Assert.Equal(creatorId, ev.CreatedByUserId);
        Assert.Equal(creatorId, Assert.Single(await check.EventEmailDeliveries.Where(x => x.EventId == id).ToListAsync()).RecipientUserId);
        Assert.Empty(await check.AgendaNotifications.Where(x => x.EventId == id).ToListAsync());
    }

    [Fact]
    public async Task EventEmailCopy_OptInDoesNotBypassTenantEvenForSuperAdministrator()
    {
        using var factory = new HemodinksApiFactory();
        using var alpha = factory.CreateClient();
        await AuthenticateAsync(alpha, Clinica.DefaultSlug);
        var beta = await SeedClinicaBetaAsync(factory);
        using var other = factory.CreateClient();
        await AuthenticateAsync(other, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        var start = DateTime.UtcNow.AddDays(1);
        using var created = await ReadJsonAsync(await other.PostAsJsonAsync("/api/events/", new
            { title = "Beta event", start, end = start.AddHours(1) }));
        var id = created.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.GetAsync($"/api/events/{id}/email-notification?copyToCreator=true")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.PostAsJsonAsync($"/api/events/{id}/email-notification", new { copyToCreator = true })).StatusCode);
    }
}
