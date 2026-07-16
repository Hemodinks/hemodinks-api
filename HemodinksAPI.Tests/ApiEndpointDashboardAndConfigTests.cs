using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task DashboardSummary_WhenAuthenticated_ReturnsSummary()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.GetAsync("/api/dashboard/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Assert.True(json.RootElement.TryGetProperty("usersCount", out _));
        Assert.True(json.RootElement.TryGetProperty("pacientesCount", out _));
        Assert.True(json.RootElement.TryGetProperty("upcomingEventsCount", out _));
    }

    [Fact]
    public async Task DashboardSummary_WhenReminderProcessorFails_ReturnsSummary()
    {
        using var factory = new HemodinksApiFactory(services =>
        {
            services.AddScoped<IEventReminderProcessor, ThrowingEventReminderProcessor>();
        });
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.GetAsync("/api/dashboard/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConfiguracoesSistema_AllowsPublicReadAndAdminUpdate()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var getResponse = await client.GetAsync("/api/configuracoes-sistema/current");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var getJson = await ReadJsonAsync(getResponse);
        Assert.Equal("Hemodinks", getJson.RootElement.GetProperty("nomeEmpresa").GetString());
        Assert.Equal(JsonValueKind.Null, getJson.RootElement.GetProperty("fotoEmpresa").ValueKind);

        await AuthenticateAsync(client);

        var updateResponse = await client.PutAsJsonAsync("/api/configuracoes-sistema/current", new
        {
            nomeEmpresa = "Clinica Alfa",
            fotoEmpresa = "data:image/png;base64,Zm90by1kYS1lbXByZXNh"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updateJson = await ReadJsonAsync(updateResponse);
        Assert.Equal("Clinica Alfa", updateJson.RootElement.GetProperty("nomeEmpresa").GetString());
        var fotoEmpresa = updateJson.RootElement.GetProperty("fotoEmpresa").GetString();
        Assert.False(string.IsNullOrWhiteSpace(fotoEmpresa));
        Assert.False(fotoEmpresa!.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(JsonValueKind.Null, updateJson.RootElement.GetProperty("dataAtualizacao").ValueKind);

        var photoResponse = await client.GetAsync("/api/configuracoes-sistema/current/foto-empresa");

        Assert.Equal(HttpStatusCode.OK, photoResponse.StatusCode);
        Assert.Equal("image/png", photoResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("foto-da-empresa", await photoResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PasswordResetFlow_WhenTokenIsValid_AllowsAuthenticationWithNewPassword()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var requestResponse = await client.PostAsJsonAsync("/api/users/password/reset", new
        {
            email = "gmarcone@gmail.com"
        });

        Assert.Equal(HttpStatusCode.OK, requestResponse.StatusCode);
        using var requestJson = await ReadJsonAsync(requestResponse);
        var token = requestJson.RootElement.GetProperty("debugToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var confirmResponse = await client.PostAsJsonAsync("/api/users/password/reset/confirm", new
        {
            token,
            novaSenha = "NovaSenha@123"
        });

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/users/authenticate", new
        {
            Email = "gmarcone@gmail.com",
            Senha = "NovaSenha@123"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

}
