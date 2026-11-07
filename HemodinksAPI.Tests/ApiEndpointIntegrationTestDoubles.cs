using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

public abstract class ApiEndpointTestBase
{
    private protected static async Task AuthenticateAsync(
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

    private protected static Task<HttpResponseMessage> PostAsJsonWithClinicHeaderAsync(
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

    private protected static Task<HttpResponseMessage> PostAsJsonWithIdempotencyKeyAsync(
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

    private protected static async Task<ClinicaBetaSeed> SeedClinicaBetaAsync(HemodinksApiFactory factory)
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

    private protected static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    private protected sealed class ThrowingEventReminderProcessor : IEventReminderProcessor
    {
        public Task<int> ProcessDueRemindersAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Falha simulada no processamento de lembretes.");
        }
    }

    private protected sealed class CapturingFileExportQueue : IFileExportQueue
    {
        public List<FileExportQueueMessage> Messages { get; } = new();

        public Task EnqueueAsync(FileExportQueueMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private protected sealed record ClinicaBetaSeed(
        int Id,
        string Slug,
        string AdminEmail,
        string AdminPassword,
        string AdminName,
        string DoctorName);

    private protected const string ValidCnpj = "11.222.333/0001-81";

    private protected static HemodinksApiFactory CreateFactoryWithPasswordResetSender(
        RecordingPasswordResetNotificationSender passwordResetSender)
    {
        return new HemodinksApiFactory(services =>
        {
            var descriptor = services.First(item => item.ServiceType == typeof(HemodinksAPI.Application.Services.IPasswordResetNotificationSender));
            services.Remove(descriptor);
            services.AddSingleton<HemodinksAPI.Application.Services.IPasswordResetNotificationSender>(passwordResetSender);
        });
    }

    private protected static async Task<FinanceiroSeed> SeedFinanceiroAsync(HemodinksApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ClinicaContext>(); tenant.SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var medico = new User { ClinicaId = Clinica.DefaultId, Nome = "Medico Financeiro", Telefone = "+5511999999999",
            Email = $"medico-{suffix}@teste.local", Senha = "hash", PerfilId = Perfil.MedicosId, Ativo = true, PrecisaTrocarSenha = false };
        var pacienteUser = new User { ClinicaId = Clinica.DefaultId, Nome = "Paciente Financeiro", Telefone = "+5511888888888",
            Email = $"paciente-{suffix}@teste.local", Senha = "hash", PerfilId = Perfil.PacientesId, Ativo = true, PrecisaTrocarSenha = false };
        var convenio = new Convenio { ClinicaId = Clinica.DefaultId, DescricaoConvenio = $"Convenio {suffix}" };
        var opme = new Opme { ClinicaId = Clinica.DefaultId, Fornecedor = $"Fornecedor OPME {suffix}" };
        db.Users.AddRange(medico, pacienteUser); db.Convenios.Add(convenio); db.OPME.Add(opme); await db.SaveChangesAsync();
        var paciente = new Paciente { ClinicaId = Clinica.DefaultId, UserId = pacienteUser.Id, NomePaciente = pacienteUser.Nome };
        db.Pacientes.Add(paciente); await db.SaveChangesAsync();
        const string code = "99999999";
        if (!await db.CbhpmGeral.AnyAsync(x => x.Codigo == code)) db.CbhpmGeral.Add(new CbhpmGeral { Codigo = code, Procedimento = "Procedimento financeiro", ValorReferencia = 1000m });
        await db.SaveChangesAsync();
        return new(paciente.Id, medico.Id, convenio.IdConvenio, opme.IdFornecedor, opme.Fornecedor, code);
    }

    private protected sealed record FinanceiroSeed(int PacienteId, int MedicoId, int ConvenioId,
        int OpmeFornecedorId, string OpmeFornecedor, string CbhpmCodigo);

    private protected sealed class PasswordPolicyLogCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception) + exception?.ToString());
        }
    }
}
