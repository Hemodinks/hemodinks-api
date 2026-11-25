using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

// Motivo de inativacao/reativacao, auditoria de alteracoes (antes/depois) e tela geral de auditoria.
public sealed class ClinicAuditDetailTests : ApiEndpointTestBase
{
    private const string Diagnostico = "Diagnostico clinico sigiloso";

    [Fact]
    public async Task InativarEReativar_ExigemMotivoDeAte500Caracteres_EGravamNaTrilha()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.InativarPacienteAsync(seed.PacienteId, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.InativarPacienteAsync(seed.PacienteId, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.InativarPacienteAsync(seed.PacienteId, new string('x', 501))).StatusCode);
        Assert.True(await IsAtivoAsync(factory, seed.PacienteId));

        Assert.Equal(HttpStatusCode.NoContent, (await client.InativarPacienteAsync(seed.PacienteId, "Paciente transferido")).StatusCode);
        Assert.False(await IsAtivoAsync(factory, seed.PacienteId));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.ReativarPacienteAsync(seed.PacienteId, "")).StatusCode);
        Assert.False(await IsAtivoAsync(factory, seed.PacienteId));
        Assert.Equal(HttpStatusCode.NoContent, (await client.ReativarPacienteAsync(seed.PacienteId, new string('r', 500))).StatusCode);

        var rows = (await ReadTrailAsync(factory)).Where(row => row.Sucesso).ToList();
        var inativar = Assert.Single(rows, row => row.Acao == "paciente.inativar");
        Assert.Equal(seed.PacienteId, inativar.PacienteId);
        Assert.Equal("Paciente transferido", Motivo(inativar));
        var reativar = Assert.Single(rows, row => row.Acao == "paciente.reativar");
        Assert.Equal(new string('r', 500), Motivo(reativar));
        // Tentativas recusadas ficam na trilha como falha, sem motivo.
        Assert.All((await ReadTrailAsync(factory)).Where(row => !row.Sucesso), row => Assert.Null(row.Detalhe));
    }

    [Fact]
    public async Task ClinicaB_NaoInativaPacienteDaA_MesmoComMotivo_Responde404SemRegistrarMotivo()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await AuthenticateAsync(client, beta.Slug);

        using var response = await client.InativarPacienteAsync(seed.PacienteId, "Tentativa indevida");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(await IsAtivoAsync(factory, seed.PacienteId));
        var row = Assert.Single(await ReadTrailAsync(factory));
        Assert.Equal(beta.Id, row.ClinicaId);
        Assert.Null(row.Detalhe);
    }

    [Fact]
    public async Task Configuracao_ComecaDesligada_SoAdministrador_E_ValeSoParaAPropriaClinica()
    {
        using var factory = new HemodinksApiFactory();
        using var admin = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await AuthenticateAsync(admin, Clinica.DefaultSlug);

        Assert.False(await AuditarAtendimentosAsync(admin));
        using var put = await admin.PutAsJsonAsync("/api/auditoria-clinica/configuracao", new { auditarAtendimentos = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.True(await AuditarAtendimentosAsync(admin));

        using var betaAdmin = factory.CreateClient();
        await AuthenticateAsync(betaAdmin, beta.Slug);
        Assert.False(await AuditarAtendimentosAsync(betaAdmin));

        using var doctor = factory.CreateClient();
        await AuthenticateAsync(doctor, beta.Slug, "dra.beta@hemodinks.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/api/auditoria-clinica/configuracao")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await doctor.PutAsJsonAsync("/api/auditoria-clinica/configuracao", new { auditarAtendimentos = true })).StatusCode);
        Assert.False(await AuditarAtendimentosAsync(betaAdmin));
    }

    [Fact]
    public async Task EdicaoDePaciente_SoGuardaAntesEDepoisComAuditoriaLigada_SemConteudoDeTextoClinico()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var url = $"/api/pacientes/{seed.PacienteId}";

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, new
        {
            nomePaciente = "Paciente Financeiro", medicoUserId = seed.MedicoId, pagamento = "1000"
        })).StatusCode);
        Assert.Null(Assert.Single(await ReadTrailAsync(factory), row => row.Acao == "paciente.editar").Detalhe);

        await SetAuditoriaAsync(client, true);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, new
        {
            nomePaciente = "Paciente Financeiro", medicoUserId = seed.MedicoId, pagamento = "1500",
            statusPago = true, convenioId = seed.ConvenioId, diagnostico = Diagnostico
        })).StatusCode);

        var row = (await ReadTrailAsync(factory)).Where(item => item.Acao == "paciente.editar").OrderBy(item => item.Id).Last();
        Assert.Equal(seed.PacienteId, row.PacienteId);
        Assert.NotNull(row.Detalhe);
        Assert.DoesNotContain(Diagnostico, row.Detalhe);
        var alteracoes = Alteracoes(row);
        Assert.Equal(("1000", "1500"), alteracoes["Pagamento"]);
        Assert.Equal(("Nao", "Sim"), alteracoes["Pago"]);
        Assert.Equal((null, null), alteracoes["Diagnostico"]);
        Assert.True(alteracoes.ContainsKey("Convenio"));
        Assert.False(alteracoes.ContainsKey("Nome"));
    }

    [Fact]
    public async Task Atendimentos_SoEntramNaTrilhaComAuditoriaLigada_ComPacienteEAlteracoes()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var semAuditoria = await CriarAtendimentoAsync(client, seed);
        Assert.DoesNotContain(await ReadTrailAsync(factory), row => row.Recurso == "atendimento");

        await SetAuditoriaAsync(client, true);
        var id = await CriarAtendimentoAsync(client, seed);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{id}", new
        {
            id, dataProcedimento = new DateTime(2026, 7, 12), medicoResponsavelId = seed.MedicoId,
            numeroAutorizacao = "AUT-2", observacao = "Texto clinico novo", procedimentos = Array.Empty<object>()
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/atendimentos-cirurgicos/{id}?motivo=Registro%20duplicado")).StatusCode);

        var rows = (await ReadTrailAsync(factory)).Where(row => row.Recurso == "atendimento").ToList();
        Assert.Equal(["atendimento.criar", "atendimento.editar", "atendimento.cancelar"], rows.Select(row => row.Acao));
        Assert.All(rows, row =>
        {
            Assert.Equal(seed.PacienteId, row.PacienteId);
            Assert.Equal(id.ToString(), row.RecursoId);
        });
        var editar = Alteracoes(rows[1]);
        Assert.Equal(("10/07/2026", "12/07/2026"), editar["Data do procedimento"]);
        Assert.Equal((null, "AUT-2"), editar["Autorizacao"]);
        Assert.Equal((null, null), editar["Observacao"]);
        Assert.DoesNotContain("Texto clinico novo", rows[1].Detalhe);
        Assert.Equal("Registro duplicado", Motivo(rows[2]));
        Assert.NotEqual(semAuditoria, id);
    }

    [Fact]
    public async Task TelaGeral_FiltraPorPacienteSomenteAlteracoesEAcao_ETrazNomeDoPaciente_SoDaPropriaClinica()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/pacientes/{seed.PacienteId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.InativarPacienteAsync(seed.PacienteId, "Obito")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditoriasClinica.Add(new AuditoriaClinica
            {
                ClinicaId = beta.Id, Acao = "paciente.inativar", Recurso = "paciente", RecursoId = seed.PacienteId.ToString(),
                PacienteId = seed.PacienteId, StatusCode = 204, Sucesso = true, DataUtc = DateTime.UtcNow,
                Detalhe = "{\"motivo\":\"motivo da beta\"}"
            });
            await db.SaveChangesAsync();
        }

        var todos = await ListAsync(client, $"pacienteId={seed.PacienteId}");
        Assert.Equal(["paciente.inativar", "paciente.ler"], todos.Select(item => item.GetProperty("acao").GetString()));
        Assert.All(todos, item => Assert.Equal("Paciente Financeiro", item.GetProperty("pacienteNome").GetString()));

        var alteracoes = await ListAsync(client, $"pacienteId={seed.PacienteId}&somenteAlteracoes=true");
        var item = Assert.Single(alteracoes);
        Assert.Contains("Obito", item.GetProperty("detalhe").GetString());

        Assert.Single(await ListAsync(client, "search=Paciente%20Financeiro&acao=paciente.inativar"));
        Assert.Empty(await ListAsync(client, "search=Outro%20Nome&acao=paciente.inativar"));
        Assert.Empty(await ListAsync(client, "periodoInicio=2000-01-01&periodoFim=2000-01-31"));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/auditoria-clinica?periodoInicio=2026-02-01&periodoFim=2026-01-01")).StatusCode);

        // A beta so ve a propria linha, e o nome do paciente da clinica A nao aparece para ela.
        using var betaClient = factory.CreateClient();
        await AuthenticateAsync(betaClient, beta.Slug);
        var betaItems = await ListAsync(betaClient, $"pacienteId={seed.PacienteId}");
        var betaItem = Assert.Single(betaItems);
        Assert.Equal(JsonValueKind.Null, betaItem.GetProperty("pacienteNome").ValueKind);
        Assert.DoesNotContain("Obito", betaItem.GetRawText());
        Assert.Empty(await ListAsync(betaClient, "search=Paciente%20Financeiro"));
    }

    [Fact]
    public void Detalhe_MuitasAlteracoes_FicaAte4000CaracteresComJsonValidoEMarcadorDeCorte()
    {
        var detalhe = new ClinicAuditDetail();
        for (var index = 0; index < 60; index++)
            detalhe.Campo($"Campo {index}", new string('a', 200), new string('b', 200));

        var json = detalhe.ToJson();
        Assert.True(json.Length <= 4000, json.Length.ToString());
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.GetProperty("alteracoes").EnumerateArray().ToList();
        Assert.Equal("Campo 0", items[0].GetProperty("campo").GetString());
        Assert.Equal("...", items[^1].GetProperty("campo").GetString());
        Assert.Equal(120 + 3, items[0].GetProperty("de").GetString()!.Length);
    }

    private static async Task<int> CriarAtendimentoAsync(HttpClient client, FinanceiroSeed seed)
    {
        using var response = await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", new
        {
            pacienteId = seed.PacienteId, dataProcedimento = new DateTime(2026, 7, 10), medicoResponsavelId = seed.MedicoId,
            procedimentos = new[] { new { cbhpmCodigo = seed.CbhpmCodigo, quantidade = 1, pesoPercentual = 100 } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task SetAuditoriaAsync(HttpClient client, bool value) =>
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/api/auditoria-clinica/configuracao", new { auditarAtendimentos = value })).StatusCode);

    private static async Task<bool> AuditarAtendimentosAsync(HttpClient client)
    {
        using var json = await ReadJsonAsync(await client.GetAsync("/api/auditoria-clinica/configuracao"));
        return json.RootElement.GetProperty("auditarAtendimentos").GetBoolean();
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"/api/auditoria-clinica?page=1&pageSize=100&{query}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static string? Motivo(AuditoriaClinica row)
    {
        using var json = JsonDocument.Parse(row.Detalhe!);
        return json.RootElement.GetProperty("motivo").GetString();
    }

    private static Dictionary<string, (string? De, string? Para)> Alteracoes(AuditoriaClinica row)
    {
        using var json = JsonDocument.Parse(row.Detalhe!);
        return json.RootElement.GetProperty("alteracoes").EnumerateArray().ToDictionary(
            item => item.GetProperty("campo").GetString()!,
            item => (Text(item, "de"), Text(item, "para")));
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<bool> IsAtivoAsync(HemodinksApiFactory factory, int pacienteId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Pacientes
            .Where(p => p.Id == pacienteId).Select(p => p.Ativo).SingleAsync();
    }

    private static async Task<List<AuditoriaClinica>> ReadTrailAsync(HemodinksApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditoriasClinica.AsNoTracking()
            .OrderBy(row => row.Id).ToListAsync();
    }
}
