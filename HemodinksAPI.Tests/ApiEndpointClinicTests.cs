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
    public async Task AuthenticateUser_WhenMultipleClinicasAndNoHintIsProvided_ReturnsBadRequest()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await SeedClinicaBetaAsync(factory);

        var response = await client.PostAsJsonAsync("/api/users/authenticate", new
        {
            Email = "gmarcone@gmail.com",
            Senha = DefaultUserPassword.Value
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Assert.Equal(
            "Clinica nao resolvida. Envie X-Clinica-Slug ou use um subdominio configurado.",
            json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AuthenticateUser_WhenClinicHeaderTargetsDuplicateEmail_ReturnsClinicScopedPayload()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);

        var response = await PostAsJsonWithClinicHeaderAsync(client, beta.Slug, "/api/users/authenticate", new
        {
            Email = beta.AdminEmail,
            Senha = beta.AdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Assert.Equal(beta.AdminEmail, json.RootElement.GetProperty("email").GetString());
        Assert.Equal(beta.AdminName, json.RootElement.GetProperty("nome").GetString());
        Assert.Equal(beta.Id, json.RootElement.GetProperty("clinicaId").GetInt32());
        Assert.Equal(beta.Slug, json.RootElement.GetProperty("clinicaSlug").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task UsersEndpoint_WhenAuthenticatedInSecondClinic_ReturnsOnlyItsOwnUsers()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);

        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var response = await client.GetAsync("/api/users/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var names = items
            .Select(item => item.GetProperty("nome").GetString())
            .Where(value => value != null)
            .Cast<string>()
            .OrderBy(value => value)
            .ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal([beta.DoctorName, beta.AdminName], names);
        Assert.DoesNotContain("George Marcone Morais dos Santos", names);
    }

    [Fact]
    public async Task AgendaEndpoint_WhenSameIdempotencyKeyIsUsedAcrossClinicas_CreatesOneEventPerClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var clientA = factory.CreateClient();
        using var clientB = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);

        await AuthenticateAsync(clientA, Clinica.DefaultSlug, "gmarcone@gmail.com", DefaultUserPassword.Value);
        await AuthenticateAsync(clientB, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var key = Guid.NewGuid().ToString("N");
        var start = DateTime.UtcNow.AddDays(3);
        var payload = new
        {
            title = "Evento multi-clinica",
            description = "Mesmo idempotency key em clinicas diferentes",
            start,
            end = start.AddHours(1),
            notifyMedicalProfile = false,
            notifyUser = true,
            reminderPeriodMinutes = 20
        };

        var responseA = await PostAsJsonWithIdempotencyKeyAsync(clientA, "/api/events/", key, payload);
        var responseB = await PostAsJsonWithIdempotencyKeyAsync(clientB, "/api/events/", key, payload);

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
        Assert.Equal("stored", responseA.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());
        Assert.Equal("stored", responseB.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
        Assert.Equal(2, dbContext.Events.Count(item => item.Title == "Evento multi-clinica"));
        Assert.Equal(2, dbContext.IdempotencyRequests.Count(item => item.Operation == "events.create"));
    }

    [Fact]
    public async Task SuperAdministrador_CanProvisionListAndNavigateNewClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, "gmarcone@gmail.com", DefaultUserPassword.Value);

        var slug = $"clinica-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica Provisionada",
            slug,
            administradorNome = "Administradora Local",
            administradorEmail = $"admin-{Guid.NewGuid():N}@example.com",
            administradorSenha = "AdminLocal@123",
            plano = "Profissional",
            assinaturaStatus = "Ativa",
            limiteUsuarios = 25
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/platform/clinicas");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using (var listJson = await ReadJsonAsync(listResponse))
        {
            Assert.Contains(listJson.RootElement.EnumerateArray(), item => item.GetProperty("slug").GetString() == slug);
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/");
        request.Headers.Add(ClinicaResolutionService.ClinicaSlugHeaderName, slug);
        var scopedResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, scopedResponse.StatusCode);
        using var scopedJson = await ReadJsonAsync(scopedResponse);
        Assert.Equal(2, scopedJson.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ClinicScopedEndpoints_DoNotExposeRecordsFromAnotherClinic()
    {
        const string marker = "TENANT-A-ONLY-MARKER";
        using var factory = new HemodinksApiFactory();
        var beta = await SeedClinicaBetaAsync(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
            var admin = new User
            {
                ClinicaId = Clinica.DefaultId,
                Nome = marker,
                Email = $"{Guid.NewGuid():N}@example.com",
                Telefone = "+5511999999901",
                Senha = "hash",
                PerfilId = Perfil.AdministradorId,
                Ativo = true
            };
            var patientUser = new User
            {
                ClinicaId = Clinica.DefaultId,
                Nome = marker,
                Email = $"{Guid.NewGuid():N}@example.com",
                Telefone = "+5511999999902",
                Senha = "hash",
                PerfilId = Perfil.PacientesId,
                Ativo = true
            };
            var patient = new Paciente
            {
                ClinicaId = Clinica.DefaultId,
                User = patientUser,
                NomePaciente = marker
            };

            context.Users.Add(admin);
            context.Pacientes.Add(patient);
            context.FaturamentosMedicos.Add(new FaturamentoMedico
            {
                ClinicaId = Clinica.DefaultId,
                Paciente = patient,
                Observacoes = marker
            });
            context.Events.Add(new Event
            {
                ClinicaId = Clinica.DefaultId,
                User = admin,
                Title = marker,
                Start = DateTime.UtcNow.AddDays(1),
                End = DateTime.UtcNow.AddDays(1).AddHours(1)
            });
            context.GruposMedicos.Add(new GrupoMedico { ClinicaId = Clinica.DefaultId, Nome = marker });
            context.Hospitais.Add(new Hospital { ClinicaId = Clinica.DefaultId, Nome = marker });
            context.Convenios.Add(new Convenio { ClinicaId = Clinica.DefaultId, DescricaoConvenio = marker });
            context.OPME.Add(new Opme { ClinicaId = Clinica.DefaultId, Fornecedor = marker });
            await context.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var scopedUrls = new[]
        {
            "/api/users/",
            "/api/pacientes/",
            "/api/faturamentos-medicos/",
            "/api/events/",
            "/api/grupos-medicos/",
            "/api/hospitais/",
            "/api/convenios/",
            "/api/opme/",
            "/api/dashboard/summary",
            "/api/dashboard/notifications"
        };

        foreach (var url in scopedUrls)
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.False(
                content.Contains(marker, StringComparison.Ordinal),
                $"A rota {url} expos dados de outra clinica: {content}");
        }
    }

}
