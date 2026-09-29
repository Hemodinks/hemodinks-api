using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task AgendaAllDay_CreateEditCivilRangeAndTenantIsolation()
    {
        using var factory = new HemodinksApiFactory();
        using var alpha = factory.CreateClient(); await AuthenticateAsync(alpha, Clinica.DefaultSlug);
        var request = new { title = "Civil date", isAllDay = true, allDayStartDate = "2033-09-26", allDayEndDate = "2033-09-26", timeZoneId = "Pacific/Kiritimati", notifyUser = true };
        using var response = await alpha.PostAsJsonAsync("/api/events/", request); response.EnsureSuccessStatusCode();
        using var created = await ReadJsonAsync(response); var id = created.RootElement.GetProperty("id").GetInt32();
        Assert.Equal("2033-09-26", created.RootElement.GetProperty("allDayStartDate").GetString());
        Assert.Equal(new DateTime(2033, 9, 25, 10, 0, 0, DateTimeKind.Utc), created.RootElement.GetProperty("start").GetDateTime());
        // A viewer at UTC-12 has no instant overlap with this UTC+14 date. Civil bounds must still return it.
        var query = "/api/events/?from=2033-09-26T12:00:00Z&to=2033-09-27T11:59:59Z&fromDate=2033-09-26&toDate=2033-09-26";
        using var visible = await ReadJsonAsync(await alpha.GetAsync(query));
        Assert.Contains(visible.RootElement.EnumerateArray(), item => item.GetProperty("id").GetInt32() == id);
        using var nextDay = await ReadJsonAsync(await alpha.GetAsync(query.Replace("fromDate=2033-09-26&toDate=2033-09-26", "fromDate=2033-09-27&toDate=2033-09-27")));
        Assert.DoesNotContain(nextDay.RootElement.EnumerateArray(), item => item.GetProperty("id").GetInt32() == id);
        using var boundary = await ReadJsonAsync(await alpha.GetAsync("/api/events/?from=2033-09-26T10:00:00Z"));
        Assert.DoesNotContain(boundary.RootElement.EnumerateArray(), item => item.GetProperty("id").GetInt32() == id);
        using var updated = await alpha.PutAsJsonAsync($"/api/events/{id}", new { request.title, request.isAllDay, request.allDayStartDate, allDayEndDate = "2033-09-28", request.timeZoneId });
        updated.EnsureSuccessStatusCode();
        using var reloaded = await ReadJsonAsync(await alpha.GetAsync($"/api/events/{id}"));
        Assert.True(reloaded.RootElement.GetProperty("isAllDay").GetBoolean());
        Assert.Equal("2033-09-28", reloaded.RootElement.GetProperty("allDayEndDate").GetString());
        Assert.Equal("Pacific/Kiritimati", reloaded.RootElement.GetProperty("timeZoneId").GetString());
        var beta = await SeedClinicaBetaAsync(factory);
        using var other = factory.CreateClient(); await AuthenticateAsync(other, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/events/{id}", request)).StatusCode);
        using var hidden = await ReadJsonAsync(await other.GetAsync(query));
        Assert.DoesNotContain(hidden.RootElement.EnumerateArray(), item => item.GetProperty("id").GetInt32() == id);
        Assert.Equal(HttpStatusCode.BadRequest, (await alpha.PostAsJsonAsync("/api/events/", new { title = "Invalid", isAllDay = true, allDayStartDate = "2033-02-30", allDayEndDate = "2033-03-01", timeZoneId = "UTC" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alpha.PostAsJsonAsync("/api/events/", new { title = "Missing dates", isAllDay = true, timeZoneId = "UTC" })).StatusCode);
    }
}
