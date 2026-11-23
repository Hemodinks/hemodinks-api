using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.HostedServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

// #368: trilha de auditoria de acesso e alteracao de dados de pacientes.
public sealed class ClinicAuditTrailTests : ApiEndpointTestBase
{
    private const string PatientName = "Paciente Trilha Sigilosa";
    private const string PatientCpf = "52998224725";
    private const string ClinicalNote = "Conteudo clinico sigiloso da observacao";

    [Fact]
    public async Task AcoesSobrePaciente_GeramUmRegistroCadaComUsuarioClinicaAcaoERecurso_SemDadoPessoal()
    {
        var storage = new InMemoryStorage();
        var exports = new CapturingFileExportQueue();
        using var factory = new HemodinksApiFactory(services =>
        {
            services.RemoveAll<IPatientFileStorage>();
            services.AddSingleton<IPatientFileStorage>(storage);
            services.RemoveAll<IFileExportQueue>();
            services.AddSingleton<IFileExportQueue>(exports);
        });
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var adminId = await AdminUserIdAsync(factory);

        using var created = await client.PostAsJsonAsync("/api/pacientes/", new
        {
            nomePaciente = PatientName, cpf = PatientCpf, medicoUserId = seed.MedicoId
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        using var createdJson = await ReadJsonAsync(created);
        var patientId = createdJson.RootElement.GetProperty("id").GetInt32();
        var id = patientId.ToString();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/pacientes/?search={PatientCpf}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/pacientes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/pacientes/{id}", new
        {
            nomePaciente = PatientName, cpf = PatientCpf, medicoUserId = seed.MedicoId
        })).StatusCode);

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("laudo")) { Headers = { ContentType = new("application/pdf") } },
            "file", "laudo-paciente-trilha-sigilosa.pdf");
        using var uploaded = await client.PostAsync($"/api/pacientes/{id}/arquivos", multipart);
        Assert.True(uploaded.StatusCode == HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());
        using var uploadedJson = await ReadJsonAsync(uploaded);
        var fileId = uploadedJson.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/pacientes/{id}/arquivos/{fileId}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/pacientes/{id}/arquivos/{fileId}")).StatusCode);

        Assert.True((await client.PostAsJsonAsync($"/api/pacientes/{id}/observacoes", new { texto = ClinicalNote })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/pacientes/{id}/observacoes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/pacientes/{id}/resumo-financeiro")).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/exports/", new
        {
            resource = "pacientes", format = "xlsx", filters = new Dictionary<string, string?> { ["search"] = PatientCpf }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/exports/", new { resource = "cbhpm", format = "xlsx" })).StatusCode);

        Assert.True((await client.DeleteAsync($"/api/pacientes/{id}")).IsSuccessStatusCode);

        var rows = await ReadTrailAsync(factory);
        Assert.All(rows, row =>
        {
            Assert.Equal(Clinica.DefaultId, row.ClinicaId);
            Assert.Equal(adminId, row.UsuarioId);
            Assert.True(row.Sucesso, $"{row.Acao} {row.StatusCode}");
        });
        var actions = rows.Select(row => $"{row.Acao}|{row.Recurso}|{row.RecursoId}").ToList();
        Assert.Equal(
        [
            $"paciente.criar|paciente|{id}",
            "paciente.listar|paciente|",
            $"paciente.ler|paciente|{id}",
            $"paciente.editar|paciente|{id}",
            $"arquivo.enviar|paciente|{id}",
            $"arquivo.baixar|paciente|{id}",
            $"arquivo.excluir|paciente|{id}",
            $"observacao.criar|paciente|{id}",
            $"observacao.listar|paciente|{id}",
            $"paciente.resumo-financeiro|paciente|{id}",
            "exportacao.solicitar|pacientes|",
            $"paciente.inativar|paciente|{id}",
        ], actions);

        var serialized = JsonSerializer.Serialize(rows);
        Assert.DoesNotContain(PatientName, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PatientCpf, serialized);
        Assert.DoesNotContain(ClinicalNote, serialized);
        Assert.DoesNotContain("laudo", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cbhpm", serialized);
    }

    [Fact]
    public async Task AcessoNegadoOuInexistente_RegistraFalhaSemRevelarDados()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/pacientes/987654")).StatusCode);

        var row = Assert.Single(await ReadTrailAsync(factory));
        Assert.Equal("paciente.ler", row.Acao);
        Assert.Equal("987654", row.RecursoId);
        Assert.Equal(404, row.StatusCode);
        Assert.False(row.Sucesso);
    }

    [Fact]
    public async Task ClinicaB_AcessandoPacienteDaClinicaA_NegadoERegistradoSoNaTrilhaDaB_ComStatusRecebidoPeloCliente()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await AuthenticateAsync(client, beta.Slug);
        var id = seed.PacienteId;

        var responses = new List<(string Action, HttpResponseMessage Response)>
        {
            ("paciente.ler", await client.GetAsync($"/api/pacientes/{id}")),
            ("paciente.editar", await client.PutAsJsonAsync($"/api/pacientes/{id}", new { nomePaciente = "Invasao", medicoUserId = seed.MedicoId })),
            ("arquivo.baixar", await client.GetAsync($"/api/pacientes/{id}/arquivos/1/download")),
            ("observacao.listar", await client.GetAsync($"/api/pacientes/{id}/observacoes")),
            ("paciente.resumo-financeiro", await client.GetAsync($"/api/pacientes/{id}/resumo-financeiro")),
            ("paciente.inativar", await client.DeleteAsync($"/api/pacientes/{id}")),
        };

        var rows = await ReadTrailAsync(factory);
        Assert.DoesNotContain(rows, row => row.ClinicaId == Clinica.DefaultId);
        Assert.Equal(responses.Count, rows.Count);
        foreach (var (action, response) in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True((int)response.StatusCode is 403 or 404, $"{action}: {(int)response.StatusCode} {body}");
            Assert.DoesNotContain("Paciente Financeiro", body);
            var row = Assert.Single(rows, item => item.Acao == action);
            Assert.Equal(beta.Id, row.ClinicaId);
            Assert.Equal(id.ToString(), row.RecursoId);
            Assert.True((int)response.StatusCode == row.StatusCode, $"{action}: cliente {(int)response.StatusCode}, trilha {row.StatusCode}");
            Assert.False(row.Sucesso);
            response.Dispose();
        }

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Pacientes.AnyAsync(p => p.Id == id));
    }

    [Fact]
    public async Task ClinicaB_NaoLeTrilhaDaClinicaA_E_NaoAdministradorRecebe403()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditoriasClinica.AddRange(
                Row(Clinica.DefaultId, "paciente.ler", "111"),
                Row(Clinica.DefaultId, "paciente.inativar", "112"),
                Row(beta.Id, "paciente.ler", "221"));
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(client, beta.Slug);
        using var betaResponse = await client.GetAsync("/api/auditoria-clinica?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, betaResponse.StatusCode);
        using var betaJson = await ReadJsonAsync(betaResponse);
        var item = Assert.Single(betaJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("221", item.GetProperty("recursoId").GetString());

        using var filtered = await client.GetAsync("/api/auditoria-clinica?page=1&pageSize=100&recurso=paciente&recursoId=111");
        using var filteredJson = await ReadJsonAsync(filtered);
        Assert.Empty(filteredJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain("112", await betaResponse.Content.ReadAsStringAsync());

        using var doctorClient = factory.CreateClient();
        await AuthenticateAsync(doctorClient, beta.Slug, "dra.beta@hemodinks.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await doctorClient.GetAsync("/api/auditoria-clinica?page=1&pageSize=10")).StatusCode);
    }

    [Fact]
    public async Task Consulta_ResolveNomeSoDeUsuarioDaPropriaClinica()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        int betaAdminId, defaultUserId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            betaAdminId = await db.Users.Where(u => u.ClinicaId == beta.Id && u.Nome == beta.AdminName).Select(u => u.Id).SingleAsync();
            defaultUserId = await db.Users.Where(u => u.ClinicaId == Clinica.DefaultId).Select(u => u.Id).FirstAsync();
            var own = Row(beta.Id, "paciente.ler", "301");
            own.UsuarioId = betaAdminId;
            var foreign = Row(beta.Id, "paciente.editar", "302");
            foreign.UsuarioId = defaultUserId;
            db.AuditoriasClinica.AddRange(own, foreign);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(client, beta.Slug);
        using var json = await ReadJsonAsync(await client.GetAsync("/api/auditoria-clinica?page=1&pageSize=10"));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var ownItem = items.Single(item => item.GetProperty("recursoId").GetString() == "301");
        var foreignItem = items.Single(item => item.GetProperty("recursoId").GetString() == "302");
        Assert.Equal(beta.AdminName, ownItem.GetProperty("usuarioNome").GetString());
        Assert.Equal(JsonValueKind.Null, foreignItem.GetProperty("usuarioNome").ValueKind);
        Assert.Equal(JsonValueKind.Null, ownItem.GetProperty("equipeOperadorNome").ValueKind);
    }

    [Fact]
    public async Task FalhaAoGravarAuditoria_NaoAfetaNemDetalhaRespostaAoCliente()
    {
        using var factory = new HemodinksApiFactory(services =>
        {
            services.RemoveAll<IClinicAuditWriter>();
            services.AddScoped<IClinicAuditWriter, ThrowingWriter>();
        });
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        using var response = await client.GetAsync("/api/pacientes/?page=1&pageSize=10");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ThrowingWriter.Secret, body);
        Assert.DoesNotContain("auditoria", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("203.0.113.77", "203.0.113.0")]
    [InlineData("::ffff:198.51.100.9", "198.51.100.0")]
    [InlineData("2001:db8:abcd:12:1:2:3:4", "2001:db8:abcd::")]
    public void IpTruncado_NaoGuardaEnderecoCompleto(string ip, string expected)
    {
        Assert.Equal(expected, ClinicAuditEndpointFilter.TruncateIp(IPAddress.Parse(ip)));
    }

    [Fact]
    public async Task Retencao_RemoveSoRegistrosAnterioresAoPrazo_DeTodasAsClinicas()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditoriasClinica.AddRange(
                Row(Clinica.DefaultId, "antigo-a", "1", now.AddDays(-31)),
                Row(beta.Id, "antigo-b", "2", now.AddDays(-40)),
                Row(Clinica.DefaultId, "recente", "3", now.AddDays(-29)));
            await db.SaveChangesAsync();
        }

        var service = new ClinicAuditRetentionHostedService(factory.Services, new ClinicAuditRetentionOptions(30, TimeSpan.FromDays(1)),
            new FixedTimeProvider(now), NullLogger<ClinicAuditRetentionHostedService>.Instance);
        Assert.Equal(2, await service.PurgeOnceAsync(CancellationToken.None));

        var remaining = await ReadTrailAsync(factory);
        Assert.Equal("recente", Assert.Single(remaining).Acao);

        var disabled = new ClinicAuditRetentionHostedService(factory.Services, new ClinicAuditRetentionOptions(0, TimeSpan.FromDays(1)),
            new FixedTimeProvider(now.AddYears(10)), NullLogger<ClinicAuditRetentionHostedService>.Instance);
        Assert.Equal(0, await disabled.PurgeOnceAsync(CancellationToken.None));
    }

    private static AuditoriaClinica Row(int clinicId, string action, string resourceId, DateTime? when = null) => new()
    {
        ClinicaId = clinicId, UsuarioId = 1, Acao = action, Recurso = "paciente", RecursoId = resourceId,
        StatusCode = 200, Sucesso = true, DataUtc = when ?? DateTime.UtcNow
    };

    private static async Task<List<AuditoriaClinica>> ReadTrailAsync(HemodinksApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditoriasClinica.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
    }

    private static async Task<int> AdminUserIdAsync(HemodinksApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(user => user.ClinicaId == Clinica.DefaultId && user.Email == "gmarcone@gmail.com")
            .Select(user => user.Id).SingleAsync();
    }

    private sealed class ThrowingWriter : IClinicAuditWriter
    {
        public const string Secret = "detalhe-interno-da-falha-de-auditoria";

        public Task WriteAsync(ClinicAuditEntry entry, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Secret);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class InMemoryStorage : IPatientFileStorage
    {
        private byte[] _content = [];

        public async Task<StoredPatientFile> SaveAsync(UploadedFile file, int clinicaId, CancellationToken cancellationToken)
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            _content = stream.ToArray();
            return new StoredPatientFile(file.FileName, file.ContentType, file.Length,
                $"https://storage.test/patient-files/clinicas/{clinicaId}/{Guid.NewGuid():N}.pdf");
        }

        public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredPatientFileContent?>(new(new MemoryStream(_content)));

        public Task DeleteAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
