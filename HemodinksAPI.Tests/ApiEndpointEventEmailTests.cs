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
    public async Task EventEmail_RequiresAuthentication_AndQueuesOnlyPersistedRecipientsInOwnClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/events/1/email-notification")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/events/1/email-notification", null)).StatusCode);
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        int recipientId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(1, Clinica.DefaultSlug);
            recipientId = (await AgendaSecurityTests.AddUser(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                "EmailRecipient", Perfil.ControllerId)).Id;
        }
        var start = DateTime.UtcNow.AddDays(1);
        using var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/events/", new
        { title = "EmailEvent", start, end = start.AddHours(1), notificationMessage = "Message", notificationUserIds = new[] { recipientId } }));
        var id = created.RootElement.GetProperty("id").GetInt32();
        using var preview = await ReadJsonAsync(await client.GetAsync($"/api/events/{id}/email-notification"));
        Assert.True(preview.RootElement.GetProperty("canSend").GetBoolean());
        Assert.Equal(recipientId, preview.RootElement.GetProperty("recipients")[0].GetProperty("userId").GetInt32());
        var queuedResponse = await client.PostAsJsonAsync($"/api/events/{id}/email-notification",
            new { clinicaId = 999, recipients = new[] { "attacker@test.local" } });
        Assert.Equal(HttpStatusCode.Accepted, queuedResponse.StatusCode);
        using var queued = await ReadJsonAsync(queuedResponse);
        Assert.Equal(1, queued.RootElement.GetProperty("queuedCount").GetInt32());
        using var duplicate = await ReadJsonAsync(await client.PostAsync($"/api/events/{id}/email-notification", null));
        Assert.Equal(0, duplicate.RootElement.GetProperty("queuedCount").GetInt32());
        using var verify = factory.Services.CreateScope();
        verify.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(1, Clinica.DefaultSlug);
        var delivery = Assert.Single(await verify.ServiceProvider.GetRequiredService<AppDbContext>().EventEmailDeliveries.ToListAsync());
        Assert.Equal(recipientId, delivery.RecipientUserId); Assert.Equal(1, delivery.ClinicaId);
        Assert.Equal("pending", delivery.Status);
    }

    [Fact]
    public async Task EventEmail_SuperAdministratorCannotAccessAnotherClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var alpha = factory.CreateClient(); await AuthenticateAsync(alpha, Clinica.DefaultSlug);
        var beta = await SeedClinicaBetaAsync(factory);
        using var other = factory.CreateClient(); await AuthenticateAsync(other, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        var start = DateTime.UtcNow.AddDays(1);
        using var created = await ReadJsonAsync(await other.PostAsJsonAsync("/api/events/", new
            { title = "PrivateBeta", start, end = start.AddHours(1) }));
        var id = created.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.GetAsync($"/api/events/{id}/email-notification")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.PostAsync($"/api/events/{id}/email-notification", null)).StatusCode);
        alpha.DefaultRequestHeaders.Add("X-Clinica-Slug", beta.Slug);
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.GetAsync($"/api/events/{id}/email-notification")).StatusCode);
    }
}


