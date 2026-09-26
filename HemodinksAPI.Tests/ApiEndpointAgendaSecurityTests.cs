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
    public async Task AgendaSecurity_CrossClinicCrudAndRecipientsAreBlocked()
    {
        using var factory = new HemodinksApiFactory();
        using var alpha = factory.CreateClient();
        await AuthenticateAsync(alpha, Clinica.DefaultSlug);
        var beta = await SeedClinicaBetaAsync(factory);
        using var betaClient = factory.CreateClient();
        await AuthenticateAsync(betaClient, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        var start = DateTime.UtcNow.AddDays(1);
        var payload = new { title = "Tenant beta event", start, end = start.AddHours(1) };
        var createdResponse = await betaClient.PostAsJsonAsync("/api/events/", payload);
        createdResponse.EnsureSuccessStatusCode();
        using var created = await ReadJsonAsync(createdResponse);
        var eventId = created.RootElement.GetProperty("id").GetInt32();
        var ownerId = created.RootElement.GetProperty("userId").GetInt32();
        int doctorId;
        int groupId;
        using (var seedScope = factory.Services.CreateScope())
        {
            seedScope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doctor = await seedDb.Users.SingleAsync(user => user.ClinicaId == beta.Id && user.PerfilId == Perfil.MedicosId);
            await HemodinksAPI.Application.Authentication.GlobalIdentityService.EnsureForUserAsync(seedDb, doctor, CancellationToken.None);
            doctorId = doctor.Id;
            var group = new GrupoMedico { ClinicaId = beta.Id, Nome = "Beta group" };
            seedDb.GruposMedicos.Add(group);
            await seedDb.SaveChangesAsync();
            groupId = group.Id;
        }

        // Alpha is a SuperAdministrador: even this profile remains in its selected clinic.
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.GetAsync($"/api/events/{eventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.PutAsJsonAsync($"/api/events/{eventId}", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.DeleteAsync($"/api/events/{eventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.PostAsync($"/api/events/{eventId}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.GetAsync($"/api/users/{ownerId}")).StatusCode);
        using var events = await ReadJsonAsync(await alpha.GetAsync("/api/events/"));
        Assert.DoesNotContain(events.RootElement.EnumerateArray(), item => item.GetProperty("id").GetInt32() == eventId);
        using var options = await ReadJsonAsync(await alpha.GetAsync("/api/events/notification-recipients"));
        Assert.DoesNotContain(options.RootElement.GetProperty("users").EnumerateArray(), item => item.GetProperty("id").GetInt32() == ownerId);
        Assert.DoesNotContain(options.RootElement.GetProperty("groups").EnumerateArray(), item => item.GetProperty("id").GetInt32() == groupId);
        using var doctors = await ReadJsonAsync(await alpha.GetAsync("/api/events/medical-users"));
        Assert.DoesNotContain(doctors.RootElement.EnumerateArray(), item => item.GetProperty("id").GetInt32() == doctorId);
        Assert.Equal(HttpStatusCode.BadRequest, (await alpha.PostAsJsonAsync("/api/events/", new
        {
            title = "Invalid medical recipient", start, end = start.AddHours(1), medicalUserId = doctorId, notifyMedicalProfile = true
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alpha.PostAsJsonAsync("/api/events/", new
        {
            title = "Invalid owner", start, end = start.AddHours(1), userId = ownerId
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await alpha.PostAsJsonAsync("/api/events/", new
        {
            title = "Invalid group", start, end = start.AddHours(1), notificationMessage = "Private", notificationGroupIds = new[] { groupId }
        })).StatusCode);

        var invalidPayload = new { title = "Invalid recipient", start, end = start.AddHours(1),
            notificationMessage = "Private", notificationUserIds = new[] { ownerId } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await alpha.PostAsJsonAsync("/api/events/", invalidPayload)).StatusCode);
        var ownResponse = await alpha.PostAsJsonAsync("/api/events/", payload);
        ownResponse.EnsureSuccessStatusCode();
        using var ownEvent = await ReadJsonAsync(ownResponse);
        var ownId = ownEvent.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Unauthorized, (await alpha.PutAsJsonAsync($"/api/events/{ownId}", invalidPayload)).StatusCode);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(beta.Id, (await db.Events.SingleAsync(ev => ev.Id == eventId)).ClinicaId);
        Assert.False((await db.Events.SingleAsync(ev => ev.Id == eventId)).IsCompleted);
        Assert.False(await db.Events.AnyAsync(ev => ev.Title == "Invalid recipient"));
        Assert.Empty(await db.AgendaNotifications.ToListAsync());
    }

    [Fact]
    public async Task AgendaSecurity_PayloadClinicIsRejectedAndHeadersCannotSwitchTenant()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        var beta = await SeedClinicaBetaAsync(factory);
        client.DefaultRequestHeaders.Add("X-Clinica-Id", beta.Id.ToString());
        client.DefaultRequestHeaders.Add("X-Clinica-Slug", beta.Slug);
        var start = DateTime.UtcNow.AddDays(1);
        var response = await client.PostAsJsonAsync("/api/events/", new
        {
            title = "Manipulated clinic", start, end = start.AddHours(1), clinicaId = beta.Id
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var validResponse = await client.PostAsJsonAsync("/api/events/", new { title = "Authenticated clinic", start, end = start.AddHours(1) });
        validResponse.EnsureSuccessStatusCode();
        using var json = await ReadJsonAsync(validResponse);
        var id = json.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/events/{id}", new
        {
            title = "Manipulated update", start, end = start.AddHours(1), clinicaId = beta.Id
        })).StatusCode);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(Clinica.DefaultId, (await db.Events.SingleAsync(ev => ev.Id == id)).ClinicaId);
    }
}
