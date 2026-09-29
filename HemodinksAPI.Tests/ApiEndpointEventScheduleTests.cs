using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task EventSchedule_RejectsInvalidPayloadWithoutWriting(string method)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var valid = new { title = "Original", start = "2026-09-26T16:00:00-03:00", end = "2026-09-26T17:00:00-03:00" };
        var created = await client.PostAsJsonAsync("/api/events/", valid);
        created.EnsureSuccessStatusCode();
        using var json = await ReadJsonAsync(created);
        var id = json.RootElement.GetProperty("id").GetInt32();
        var cases = new (string Key, object? Value)[]
        {
            ("title", "   "), ("end", valid.start), ("end", "2026-09-26T15:59:00-03:00"),
            ("start", "2026-02-30T16:00:00Z"), ("end", "2026-13-01T16:00:00Z"),
            ("start", "2026-09-26T25:00:00Z"), ("end", "2026-09-26T17:60:00Z"),
            ("start", null), ("end", null), ("start", "0001-01-01T00:00:00Z")
        };
        foreach (var (key, value) in cases)
        {
            var payload = new Dictionary<string, object?> { ["title"] = valid.title, ["start"] = valid.start, ["end"] = valid.end };
            if (value == null) payload.Remove(key); else payload[key] = value;
            var response = method == "POST" ? await client.PostAsJsonAsync("/api/events/", payload)
                : await client.PutAsJsonAsync($"/api/events/{id}", payload);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("System.Text.Json", body);
            Assert.DoesNotContain("stackTrace", body);
        }
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var ev = Assert.Single(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Events.ToListAsync());
        Assert.Equal("Original", ev.Title);
        Assert.Equal(new DateTime(2026, 9, 26, 19, 0, 0, DateTimeKind.Utc), ev.Start);
        Assert.Equal(new DateTime(2026, 9, 26, 20, 0, 0, DateTimeKind.Utc), ev.End);
    }

    [Fact]
    public async Task EventSchedule_CreateEditAndReadPreserveCrossDayInstantsAndOffset()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var created = await client.PostAsJsonAsync("/api/events/", new
        {
            title = "Overnight", start = "2026-09-26T23:30:00-03:00", end = "2026-09-27T00:30:00-03:00"
        });
        created.EnsureSuccessStatusCode();
        using var json = await ReadJsonAsync(created);
        var id = json.RootElement.GetProperty("id").GetInt32();
        Assert.Equal("2026-09-27T02:30:00Z", json.RootElement.GetProperty("start").GetString());
        var updated = await client.PutAsJsonAsync($"/api/events/{id}", new
        {
            title = "Edited overnight", start = "2026-09-27T02:30:00Z", end = "2026-09-27T02:00:00-03:00"
        });
        updated.EnsureSuccessStatusCode();
        using var read = await ReadJsonAsync(await client.GetAsync($"/api/events/{id}"));
        Assert.Equal("2026-09-27T02:30:00Z", read.RootElement.GetProperty("start").GetString());
        Assert.Equal("2026-09-27T05:00:00Z", read.RootElement.GetProperty("end").GetString());
    }
}
