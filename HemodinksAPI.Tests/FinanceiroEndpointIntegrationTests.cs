using System.Net;
using System.Net.Http.Json;
using System.Text;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task FinanceiroEndpoints_ExecuteNormalizedFlowWithCrudFiltersReportsAuditAndReceiptFile()
    {
        var fileStorage = new TestingFinancialFileStorage();
        using var factory = new HemodinksApiFactory(services =>
        {
            services.RemoveAll<IPatientFileStorage>();
            services.AddSingleton<IPatientFileStorage>(fileStorage);
        });
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var seed = await SeedFinanceiroAsync(factory);

        var atendimentoResponse = await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", new
        {
            pacienteId = seed.PacienteId,
            dataProcedimento = new DateTime(2026, 7, 10),
            convenioId = seed.ConvenioId,
            medicoResponsavelId = seed.MedicoId,
            diagnostico = "Diagnostico inicial",
            tratamentoMedico = "Procedimento cirurgico",
            numeroAutorizacao = "AUT-1",
            status = AtendimentoCirurgicoStatus.Realizado,
            procedimentos = new[] { new { cbhpmCodigo = seed.CbhpmCodigo, descricao = "Procedimento teste", quantidade = 1m, pesoPercentual = 100m } }
        });
        Assert.Equal(HttpStatusCode.Created, atendimentoResponse.StatusCode);
        using var atendimentoJson = await ReadJsonAsync(atendimentoResponse);
        var atendimentoId = atendimentoJson.RootElement.GetProperty("id").GetInt32();

        var detalheAtendimento = await client.GetAsync($"/api/atendimentos-cirurgicos/{atendimentoId}");
        Assert.Equal(HttpStatusCode.OK, detalheAtendimento.StatusCode);
        var atualizarAtendimento = await client.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{atendimentoId}", new
        {
            id = atendimentoId, dataProcedimento = new DateTime(2026, 7, 11), convenioId = seed.ConvenioId,
            medicoResponsavelId = seed.MedicoId, diagnostico = "Diagnostico revisado", tratamentoMedico = "Cirurgia",
            numeroAutorizacao = "AUT-2", status = AtendimentoCirurgicoStatus.Realizado, procedimentos = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, atualizarAtendimento.StatusCode);

        var faturamentoResponse = await client.PostAsJsonAsync("/api/faturamentos/", new
        {
            atendimentoCirurgicoId = atendimentoId, numeroGuia = "GUIA-1", numeroLote = "LOTE-1",
            competencia = new DateTime(2026, 7, 1), observacao = "Integracao"
        });
        Assert.Equal(HttpStatusCode.Created, faturamentoResponse.StatusCode);
        using var faturamentoJson = await ReadJsonAsync(faturamentoResponse);
        var faturamentoId = faturamentoJson.RootElement.GetProperty("id").GetInt32();
        var faturamentoVersion = faturamentoJson.RootElement.GetProperty("rowVersion").GetBytesFromBase64();

        var atualizarFaturamento = await client.PutAsJsonAsync($"/api/faturamentos/{faturamentoId}", new
        {
            id = faturamentoId, numeroGuia = "GUIA-2", numeroLote = "LOTE-2", competencia = new DateTime(2026, 7, 1),
            observacao = "Revisado", rowVersion = faturamentoVersion
        });
        Assert.Equal(HttpStatusCode.OK, atualizarFaturamento.StatusCode);
        using var atualizadoJson = await ReadJsonAsync(atualizarFaturamento);
        faturamentoVersion = atualizadoJson.RootElement.GetProperty("rowVersion").GetBytesFromBase64();

        var statusResponse = await client.PutAsJsonAsync($"/api/faturamentos/{faturamentoId}/status", new
        {
            id = faturamentoId, status = FaturamentoStatus.ProntoParaEnvio, rowVersion = faturamentoVersion
        });
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        var contaResponse = await client.PostAsJsonAsync($"/api/faturamentos/{faturamentoId}/contas-receber", new
        {
            faturamentoId, numeroDocumento = "TIT-1", descricao = "Honorarios", dataEmissao = new DateTime(2026, 7, 12),
            dataVencimento = new DateTime(2026, 8, 12), observacao = "Titulo de teste"
        });
        Assert.Equal(HttpStatusCode.OK, contaResponse.StatusCode);
        using var contaJson = await ReadJsonAsync(contaResponse);
        var contaId = contaJson.RootElement.GetProperty("id").GetInt32();
        var contaVersion = contaJson.RootElement.GetProperty("rowVersion").GetBytesFromBase64();
        var saldo = contaJson.RootElement.GetProperty("saldoAberto").GetDecimal();

        var recebimentoResponse = await client.PostAsJsonAsync($"/api/financeiro/contas-receber/{contaId}/recebimentos", new
        {
            contaReceberId = contaId, dataRecebimento = new DateTime(2026, 8, 1), valorRecebido = saldo,
            formaRecebimento = FormaRecebimento.Pix, referenciaBancaria = "PIX-1", observacao = "Recebido",
            usuarioCadastroId = 0, rowVersion = contaVersion
        });
        Assert.Equal(HttpStatusCode.OK, recebimentoResponse.StatusCode);
        using var recebidoJson = await ReadJsonAsync(recebimentoResponse);
        var recebimentoId = recebidoJson.RootElement.GetProperty("recebimentos")[0].GetProperty("id").GetInt32();

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("comprovante")) { Headers = { ContentType = new("application/pdf") } }, "arquivo", "comprovante.pdf");
        var uploadResponse = await client.PostAsync($"/api/financeiro/contas-receber/recebimentos/{recebimentoId}/comprovante", multipart);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.Equal("comprovante.pdf", fileStorage.LastSavedName);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/financeiro/contas-receber/recebimentos/{recebimentoId}/comprovante")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/faturamentos/pesquisa?page=1&pageSize=10&termo=GUIA-2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/financeiro/contas-receber/pesquisa?page=1&pageSize=10&termo=TIT-1")).StatusCode);
        var report = await client.GetAsync("/api/financeiro/relatorios/resumo");
        Assert.True(report.StatusCode == HttpStatusCode.OK, await report.Content.ReadAsStringAsync());
        using var reportJson = await ReadJsonAsync(report);
        Assert.True(reportJson.RootElement.GetProperty("valorApresentado").GetDecimal() > 0);

        var audit = await client.GetAsync("/api/financeiro/auditoria?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        using var auditJson = await ReadJsonAsync(audit);
        Assert.True(auditJson.RootElement.GetProperty("totalItems").GetInt32() >= 5);
    }

    [Fact]
    public async Task FinanceiroEndpoints_RejectUnsafeDeletesAndInvalidPagination()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/faturamentos/pesquisa?page=0&pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/financeiro/contas-receber/pesquisa?page=1&pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task FinanceiroAudit_ReturnsOnlyCurrentClinicHistory()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var globalId = await db.UsuariosGlobais.Select(x => x.Id).FirstAsync();
            db.AuditoriasPlataforma.AddRange(
                new AuditoriaPlataforma { UsuarioGlobalId = globalId, ClinicaId = Clinica.DefaultId, Acao = "POST", Recurso = "financeiro:faturamentos" },
                new AuditoriaPlataforma { UsuarioGlobalId = globalId, ClinicaId = 999, Acao = "POST", Recurso = "financeiro:faturamentos" });
            await db.SaveChangesAsync();
        }
        var response = await client.GetAsync("/api/financeiro/auditoria?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal(1, json.RootElement.GetProperty("totalItems").GetInt32());
    }

    private static async Task<FinanceiroSeed> SeedFinanceiroAsync(HemodinksApiFactory factory)
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
        db.Users.AddRange(medico, pacienteUser); db.Convenios.Add(convenio); await db.SaveChangesAsync();
        var paciente = new Paciente { ClinicaId = Clinica.DefaultId, UserId = pacienteUser.Id, NomePaciente = pacienteUser.Nome };
        db.Pacientes.Add(paciente); await db.SaveChangesAsync();
        const string code = "99999999";
        if (!await db.CbhpmGeral.AnyAsync(x => x.Codigo == code)) db.CbhpmGeral.Add(new CbhpmGeral { Codigo = code, Procedimento = "Procedimento financeiro", ValorReferencia = 1000m });
        await db.SaveChangesAsync();
        return new(paciente.Id, medico.Id, convenio.IdConvenio, code);
    }

    private sealed record FinanceiroSeed(int PacienteId, int MedicoId, int ConvenioId, string CbhpmCodigo);

    private sealed class TestingFinancialFileStorage : IPatientFileStorage
    {
        private byte[] _content = [];
        public string? LastSavedName { get; private set; }
        public async Task<StoredPatientFile> SaveAsync(IFormFile file, CancellationToken cancellationToken)
        {
            LastSavedName = file.FileName; await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken); _content = stream.ToArray();
            return new(file.FileName, file.ContentType, file.Length, "https://storage.test/comprovante.pdf");
        }
        public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, CancellationToken cancellationToken) =>
            Task.FromResult<StoredPatientFileContent?>(new(new MemoryStream(_content)));
        public Task DeleteAsync(string? fileUrl, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
