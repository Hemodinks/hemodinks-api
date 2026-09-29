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
    public async Task AgendaRecipients_SearchCannotExposeOtherClinic_AndReminderEditsPersist()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        var beta = await SeedClinicaBetaAsync(factory);
        int localId;
        int foreignId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            localId = (await AgendaSecurityTests.AddUser(db, "RecipientLocal", Perfil.MedicosId)).Id;
            foreignId = (await AgendaSecurityTests.AddUser(db, "RecipientOther", Perfil.MedicosId, beta.Id)).Id;
        }
        using var found = await ReadJsonAsync(await client.GetAsync("/api/events/notification-recipients?search=Recipient&profile=medical&page=1&pageSize=1"));
        Assert.Equal(1, found.RootElement.GetProperty("totalUsers").GetInt32());
        Assert.Equal(localId, Assert.Single(found.RootElement.GetProperty("users").EnumerateArray()).GetProperty("id").GetInt32());
        using var hidden = await ReadJsonAsync(await client.GetAsync("/api/events/notification-recipients?search=RecipientOther&profile=medical"));
        Assert.Equal(0, hidden.RootElement.GetProperty("totalUsers").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/events/notification-recipients?pageSize=500")).StatusCode);
        var start = DateTime.UtcNow.AddDays(10);
        var bad = await client.PostAsJsonAsync("/api/events/", new { title = "Invalid", start, end = start.AddHours(1),
            notificationMessage = "Message", notificationUserIds = new[] { foreignId } });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        var created = await client.PostAsJsonAsync("/api/events/", new { title = "Reminder recipients", start, end = start.AddHours(1),
            notifyUser = true, notifyMedicalProfile = true, medicalUserId = localId, reminderPeriodMinutes = 15,
            notificationMessage = "Message", notificationUserIds = new[] { localId } });
        created.EnsureSuccessStatusCode();
        using var data = await ReadJsonAsync(created);
        var id = data.RootElement.GetProperty("id").GetInt32();
        using var persisted = await ReadJsonAsync(await client.GetAsync($"/api/events/{id}"));
        Assert.True(persisted.RootElement.GetProperty("notifyUser").GetBoolean());
        Assert.Equal(15, persisted.RootElement.GetProperty("reminderPeriodMinutes").GetInt32());
        Assert.Equal(localId, persisted.RootElement.GetProperty("medicalUserId").GetInt32());
        var update = await client.PutAsJsonAsync($"/api/events/{id}", new { title = "Reminder recipients", start, end = start.AddHours(1), notifyUser = false, notifyMedicalProfile = false });
        update.EnsureSuccessStatusCode();
        using var edited = await ReadJsonAsync(await client.GetAsync($"/api/events/{id}"));
        Assert.False(edited.RootElement.GetProperty("notifyUser").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, edited.RootElement.GetProperty("nextReminderAt").ValueKind);
        using var allResponse = await client.PostAsJsonAsync("/api/events/", new { title = "All allowed", start, end = start.AddHours(1),
            notifyAllAllowedRecipients = true, notificationMessage = "All users message" });
        allResponse.EnsureSuccessStatusCode();
        using var allData = await ReadJsonAsync(allResponse);
        var allId = allData.RootElement.GetProperty("id").GetInt32();
        using var finalScope = factory.Services.CreateScope();
        finalScope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await finalDb.AgendaNotifications.AnyAsync(n => n.EventId == allId && n.RecipientUserId == localId));
        Assert.False(await finalDb.AgendaNotifications.AnyAsync(n => n.EventId == allId && n.RecipientUserId == foreignId));
        Assert.Single(await finalDb.AgendaNotifications.Where(n => n.EventId == id && n.RecipientUserId == localId).ToListAsync());
        Assert.False(await finalDb.AgendaNotifications.AnyAsync(n => n.EventId == id && n.RecipientUserId == foreignId));
    }
}
