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
    public async Task AgendaSecurity_NotificationsRemainInRecipientClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var alpha = factory.CreateClient();
        await AuthenticateAsync(alpha, Clinica.DefaultSlug);
        var beta = await SeedClinicaBetaAsync(factory);
        using var betaClient = factory.CreateClient();
        await AuthenticateAsync(betaClient, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        int recipientId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            recipientId = (await AgendaSecurityTests.AddUser(db, "recipient", Perfil.ControllerId, beta.Id)).Id;
        }
        var start = DateTime.UtcNow.AddDays(1);
        var response = await betaClient.PostAsJsonAsync("/api/events/", new
        {
            title = "BETA PRIVATE NOTIFICATION", start, end = start.AddHours(1),
            notificationMessage = "BETA PRIVATE MESSAGE", notificationUserIds = new[] { recipientId, recipientId }
        });
        response.EnsureSuccessStatusCode();
        var dashboard = await alpha.GetAsync("/api/dashboard/notifications");
        dashboard.EnsureSuccessStatusCode();
        Assert.DoesNotContain("BETA PRIVATE", await dashboard.Content.ReadAsStringAsync());
        var markRead = await alpha.PostAsync("/api/events/notifications/mark-read", null);
        markRead.EnsureSuccessStatusCode();
        using var verification = factory.Services.CreateScope();
        verification.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = Assert.Single(await verifyDb.AgendaNotifications.ToListAsync());
        Assert.Equal(beta.Id, notification.ClinicaId);
        Assert.Equal(recipientId, notification.RecipientUserId);
        Assert.Null(notification.ReadAt);
    }
}
