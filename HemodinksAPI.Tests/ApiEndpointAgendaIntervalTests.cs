using System.Net.Http.Json;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task AgendaInterval_ReturnsOverlapsAndLastDayEveningOnlyWithinRange()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        var first = new DateTime(2032, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var last = first.AddDays(42).AddMilliseconds(-1);
        var cases = new[]
        {
            (Title: "Before grid", Start: first.AddDays(-2), End: first.AddDays(-1), Included: false),
            (Title: "Crossing grid start", Start: first.AddHours(-1), End: first.AddHours(1), Included: true),
            (Title: "Inside grid", Start: first.AddDays(10), End: first.AddDays(10).AddHours(1), Included: true),
            (Title: "Last evening", Start: last.Date.AddHours(23), End: last.Date.AddHours(23.5), Included: true),
            (Title: "After grid", Start: first.AddDays(42), End: first.AddDays(42).AddHours(1), Included: false)
        };
        foreach (var item in cases)
        {
            using var created = await client.PostAsJsonAsync("/api/events/", new
            {
                title = item.Title, start = item.Start, end = item.End
            });
            created.EnsureSuccessStatusCode();
        }
        using var response = await client.GetAsync($"/api/events/?from={Uri.EscapeDataString(first.ToString("O"))}&to={Uri.EscapeDataString(last.ToString("O"))}");
        response.EnsureSuccessStatusCode();
        using var json = await ReadJsonAsync(response);
        var titles = json.RootElement.EnumerateArray().Select(item => item.GetProperty("title").GetString()).ToList();
        foreach (var item in cases)
            Assert.Equal(item.Included, titles.Contains(item.Title));
    }
}
