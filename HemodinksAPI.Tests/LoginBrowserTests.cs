using System.Diagnostics;
using System.Text.Json;
using HemodinksAPI.Domain.Models;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

// Each scenario owns a fresh API and database. Production rate limits remain enabled.
public sealed class LoginBrowserTests
{
    [Theory]
    [InlineData("individual")]
    [InlineData("selection")]
    [InlineData("pin")]
    [InlineData("invalid-pin")]
    [InlineData("anonymous")]
    [InlineData("anonymous-write")]
    [InlineData("cross-tenant")]
    [InlineData("operator-swap")]
    [InlineData("replay")]
    [InlineData("cancel")]
    [InlineData("layout")]
    public async Task Browser_UsesRealApi(string scenario)
    {
        var frontPath = Environment.GetEnvironmentVariable("HEMODINKS_E2E_FRONT_PATH");
        if (string.IsNullOrWhiteSpace(frontPath))
            Assert.Skip("Set HEMODINKS_E2E_FRONT_PATH to the frontend checkout to run Playwright against an isolated real API.");

        using var factory = new HemodinksApiFactory(services =>
            services.PostConfigure<CorsOptions>(options => options.AddPolicy("Frontend", policy =>
                policy.WithOrigins("http://127.0.0.1:5184").AllowAnyHeader().AllowAnyMethod().AllowCredentials())));
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = Path.GetFullPath(frontPath!),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "node_modules/@playwright/test/cli.js", "test", "--config=playwright.login.config.ts", "--grep", $"case:{scenario}$" })
            start.ArgumentList.Add(argument);
        start.Environment["VITE_API_URL"] = address;
        start.Environment["HEMODINKS_E2E_SCENARIO"] = scenario;
        start.Environment["HEMODINKS_LOGIN_FIXTURE"] = JsonSerializer.Serialize(new
        {
            apiUrl = address, fixture.Selection, fixture.Pin, fixture.Anonymous, fixture.Other,
            fixture.OtherPatientId, otherPatientName = TeamLoginFixture.OtherPatientName,
            password = TeamLoginFixture.Password, pinValue = TeamLoginFixture.PinValue,
            individual = new { email = "gmarcone@gmail.com", password = TestPasswords.Valid, clinicId = Clinica.DefaultId }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, (await output) + (await error));
    }
}
