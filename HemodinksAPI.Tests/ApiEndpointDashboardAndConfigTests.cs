using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Application.Services;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task ConfiguracoesSistema_AllowsPublicReadAndRejectsLegacyBrandUpdate()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var getResponse = await client.GetAsync("/api/configuracoes-sistema/current");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var getJson = await ReadJsonAsync(getResponse);
        Assert.Equal("HemoDinks", getJson.RootElement.GetProperty("nomeEmpresa").GetString());
        Assert.Equal(JsonValueKind.Null, getJson.RootElement.GetProperty("fotoEmpresa").ValueKind);

        await AuthenticateAsync(client);

        var updateResponse = await client.PutAsJsonAsync("/api/configuracoes-sistema/current", new
        {
            nomeEmpresa = "Clinica Alfa",
            fotoEmpresa = "data:image/png;base64,Zm90by1kYS1lbXByZXNh"
        });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, updateResponse.StatusCode);

        var photoResponse = await client.GetAsync("/api/configuracoes-sistema/current/foto-empresa");

        Assert.Equal(HttpStatusCode.NotFound, photoResponse.StatusCode);
    }

    [Fact]
    public async Task MonitoringErrors_RequiresAdministratorAndReturnsPagedResult()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var unauthorizedResponse = await client.GetAsync("/api/monitoramento/erros");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);
        var unauthorizedClearResponse = await client.DeleteAsync("/api/monitoramento/erros");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedClearResponse.StatusCode);

        await AuthenticateAsync(client);
        var response = await client.GetAsync("/api/monitoramento/erros?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal(1, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, json.RootElement.GetProperty("pageSize").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task PasswordResetFlow_WhenTokenIsValid_AllowsAuthenticationWithNewPassword()
    {
        var passwordResetSender = new RecordingPasswordResetNotificationSender();
        using var factory = new HemodinksApiFactory(services =>
        {
            var descriptor = services.First(item => item.ServiceType == typeof(IPasswordResetNotificationSender));
            services.Remove(descriptor);
            services.AddSingleton<IPasswordResetNotificationSender>(passwordResetSender);
        });
        using var client = factory.CreateClient();

        var requestResponse = await client.PostAsJsonAsync("/api/users/password/reset", new
        {
            email = "gmarcone@gmail.com"
        });

        Assert.Equal(HttpStatusCode.OK, requestResponse.StatusCode);
        var token = passwordResetSender.Notifications.Single().Token;

        var confirmResponse = await client.PostAsJsonAsync("/api/users/password/reset/confirm", new
        {
            token,
            novaSenha = "NovaTestPassword@123"
        });

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/users/authenticate", new
        {
            Email = "gmarcone@gmail.com",
            Senha = "NovaTestPassword@123"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

}
