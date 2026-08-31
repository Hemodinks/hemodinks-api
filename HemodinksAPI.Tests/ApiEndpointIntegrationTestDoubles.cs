using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    private static async Task AuthenticateAsync(
        HttpClient client,
        string? clinicaSlug = null,
        string email = "gmarcone@gmail.com",
        string? senha = null)
    {
        senha ??= TestPasswords.Valid;
        var response = await PostAsJsonWithClinicHeaderAsync(client, clinicaSlug, "/api/users/authenticate", new
        {
            Email = email,
            Senha = senha
        });

        response.EnsureSuccessStatusCode();

        using var json = await ReadJsonAsync(response);
        var token = json.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static Task<HttpResponseMessage> PostAsJsonWithClinicHeaderAsync(
        HttpClient client,
        string? clinicaSlug,
        string uri,
        object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload)
        };

        if (!string.IsNullOrWhiteSpace(clinicaSlug))
        {
            request.Headers.Add(ClinicaResolutionService.ClinicaSlugHeaderName, clinicaSlug);
        }

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostAsJsonWithIdempotencyKeyAsync(
        HttpClient client,
        string uri,
        string idempotencyKey,
        object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Add(RequestIdempotencyService.IdempotencyKeyHeaderName, idempotencyKey);
        return client.SendAsync(request);
    }

    private static async Task<ClinicaBetaSeed> SeedClinicaBetaAsync(HemodinksApiFactory factory)
    {
        const int clinicaId = 2;
        const string clinicaSlug = "clinica-beta";
        const string adminEmail = "gmarcone@gmail.com";
        // A credencial pertence a identidade global, nao a cada clinica.
        var adminPassword = TestPasswords.Valid;
        const string adminName = "George Beta";
        const string doctorName = "Dra. Beta";

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();

        if (await dbContext.Clinicas.AnyAsync(item => item.Id == clinicaId))
        {
            return new ClinicaBetaSeed(clinicaId, clinicaSlug, adminEmail, adminPassword, adminName, doctorName);
        }

        var passwordHasher = new PasswordHasher();
        dbContext.Clinicas.Add(new Clinica
        {
            Id = clinicaId,
            Nome = "Clinica Beta",
            Slug = clinicaSlug,
            Ativa = true,
            DataCadastro = DateTime.UtcNow
        });

        dbContext.Users.AddRange(
            new User
            {
                ClinicaId = clinicaId,
                Nome = adminName,
                Email = adminEmail,
                Telefone = "+5511990000001",
                Cpf = "12345678901",
                Senha = passwordHasher.HashPassword(adminPassword),
                DataNascimento = new DateTime(1982, 2, 25),
                DataCadastro = DateTime.UtcNow,
                Ativo = true,
                PrecisaTrocarSenha = false,
                PerfilId = Perfil.AdministradorId
            },
            new User
            {
                ClinicaId = clinicaId,
                Nome = doctorName,
                Email = "dra.beta@hemodinks.com",
                Telefone = "+5511990000002",
                Cpf = "12345678902",
                Crm = "99887",
                CrmUf = "SP",
                Senha = passwordHasher.HashPassword(TestPasswords.Valid),
                DataNascimento = new DateTime(1988, 5, 10),
                DataCadastro = DateTime.UtcNow,
                Ativo = true,
                PrecisaTrocarSenha = false,
                PerfilId = Perfil.MedicosId
            });

        await dbContext.SaveChangesAsync();

        return new ClinicaBetaSeed(clinicaId, clinicaSlug, adminEmail, adminPassword, adminName, doctorName);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    private sealed class ThrowingEventReminderProcessor : IEventReminderProcessor
    {
        public Task<int> ProcessDueRemindersAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Falha simulada no processamento de lembretes.");
        }
    }

    private sealed class CapturingFileExportQueue : IFileExportQueue
    {
        public List<FileExportQueueMessage> Messages { get; } = new();

        public Task EnqueueAsync(FileExportQueueMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed record ClinicaBetaSeed(
        int Id,
        string Slug,
        string AdminEmail,
        string AdminPassword,
        string AdminName,
        string DoctorName);
}
