using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task ArquivoDoHistorico_PodeSerEnviadoListadoBaixadoEExcluidoPorMes()
    {
        var fileStorage = new TestingFinancialFileStorage();
        using var factory = new HemodinksApiFactory(services =>
        {
            services.RemoveAll<IPatientFileStorage>();
            services.AddSingleton<IPatientFileStorage>(fileStorage);
        });
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        using var attachment = new MultipartFormDataContent();
        attachment.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes("relatorio mensal"))
            {
                Headers = { ContentType = new("application/pdf") }
            },
            "arquivo",
            "historico-julho.pdf");

        var uploadResponse = await client.PostAsync(
            "/api/faturamentos-medicos/historico/2026/7/arquivos",
            attachment);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        using var uploadJson = await ReadJsonAsync(uploadResponse);
        var fileId = uploadJson.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(2026, uploadJson.RootElement.GetProperty("ano").GetInt32());
        Assert.Equal(7, uploadJson.RootElement.GetProperty("mes").GetInt32());

        var list = await client.GetFromJsonAsync<JsonElement[]>(
            "/api/faturamentos-medicos/historico/arquivos?ano=2026&mes=7");
        Assert.NotNull(list);
        Assert.Contains(list, item => item.GetProperty("id").GetInt32() == fileId);

        var downloadResponse = await client.GetAsync(
            $"/api/faturamentos-medicos/historico/arquivos/{fileId}/download");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("relatorio mensal", await downloadResponse.Content.ReadAsStringAsync());

        var deleteResponse = await client.DeleteAsync(
            $"/api/faturamentos-medicos/historico/arquivos/{fileId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.True(fileStorage.DeleteCalled);
    }

    [Fact]
    public async Task GlosaDoAtendimento_PropagaParaFaturamentoPopupEFinanceiro()
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
            observacao = "Conferir documentacao antes do faturamento.",
            valorGlosa = 200m,
            motivoGlosa = "Divergencia contratual",
            status = AtendimentoCirurgicoStatus.Realizado,
            procedimentos = new[]
            {
                new
                {
                    cbhpmCodigo = seed.CbhpmCodigo,
                    descricao = "Procedimento teste",
                    quantidade = 1m,
                    pesoPercentual = 100m
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, atendimentoResponse.StatusCode);
        using var atendimentoJson = await ReadJsonAsync(atendimentoResponse);
        var atendimentoId = atendimentoJson.RootElement.GetProperty("id").GetInt32();
        Assert.Equal("Conferir documentacao antes do faturamento.",
            atendimentoJson.RootElement.GetProperty("observacao").GetString());

        using var attachment = new MultipartFormDataContent();
        attachment.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes("laudo"))
            {
                Headers = { ContentType = new("application/pdf") }
            },
            "arquivo",
            "laudo-atendimento.pdf");
        var uploadResponse = await client.PostAsync(
            $"/api/atendimentos-cirurgicos/{atendimentoId}/arquivos",
            attachment);

        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        using var uploadJson = await ReadJsonAsync(uploadResponse);
        var arquivoId = uploadJson.RootElement.GetProperty("id").GetInt32();
        Assert.Equal("laudo-atendimento.pdf", uploadJson.RootElement.GetProperty("nomeOriginal").GetString());
        Assert.Equal("laudo-atendimento.pdf", fileStorage.LastSavedName);
        var downloadResponse = await client.GetAsync(
            $"/api/atendimentos-cirurgicos/{atendimentoId}/arquivos/{arquivoId}/download");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("application/pdf", downloadResponse.Content.Headers.ContentType?.MediaType);

        var faturamentoResponse = await client.PostAsJsonAsync("/api/faturamentos/", new
        {
            atendimentoCirurgicoId = atendimentoId,
            numeroGuia = "GUIA-GLOSA",
            competencia = new DateTime(2026, 7, 1)
        });

        Assert.Equal(HttpStatusCode.Created, faturamentoResponse.StatusCode);
        using var faturamentoJson = await ReadJsonAsync(faturamentoResponse);
        var faturamentoId = faturamentoJson.RootElement.GetProperty("id").GetInt32();
        var faturamentoVersion = faturamentoJson.RootElement.GetProperty("rowVersion").GetBytesFromBase64();
        Assert.Equal(200m, faturamentoJson.RootElement.GetProperty("valorGlosado").GetDecimal());
        Assert.Equal(800m, faturamentoJson.RootElement.GetProperty("valorReconhecido").GetDecimal());
        Assert.Equal("Divergencia contratual",
            faturamentoJson.RootElement.GetProperty("glosas")[0].GetProperty("descricaoMotivo").GetString());

        var readyResponse = await client.PutAsJsonAsync($"/api/faturamentos/{faturamentoId}/status", new
        {
            id = faturamentoId,
            status = FaturamentoStatus.ProntoParaEnvio,
            rowVersion = faturamentoVersion
        });
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);

        var contaResponse = await client.PostAsJsonAsync($"/api/faturamentos/{faturamentoId}/contas-receber", new
        {
            faturamentoId,
            numeroDocumento = "TIT-GLOSA",
            descricao = "Honorarios com glosa antecipada",
            dataEmissao = new DateTime(2026, 7, 12),
            dataVencimento = new DateTime(2026, 8, 12)
        });

        Assert.Equal(HttpStatusCode.OK, contaResponse.StatusCode);
        using var contaJson = await ReadJsonAsync(contaResponse);
        var contaId = contaJson.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(1000m, contaJson.RootElement.GetProperty("valorOriginal").GetDecimal());
        Assert.Equal(800m, contaJson.RootElement.GetProperty("valorAjustado").GetDecimal());
        Assert.Equal(800m, contaJson.RootElement.GetProperty("saldoAberto").GetDecimal());

        var atualizarAtendimento = await client.PutAsJsonAsync(
            $"/api/atendimentos-cirurgicos/{atendimentoId}",
            new
            {
                id = atendimentoId,
                dataProcedimento = new DateTime(2026, 7, 10),
                convenioId = seed.ConvenioId,
                medicoResponsavelId = seed.MedicoId,
                observacao = "Glosa revisada antes do fechamento.",
                valorGlosa = 300m,
                motivoGlosa = "Divergencia revisada",
                status = AtendimentoCirurgicoStatus.Realizado,
                procedimentos = Array.Empty<object>()
            });

        Assert.Equal(HttpStatusCode.OK, atualizarAtendimento.StatusCode);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var faturamentoAtualizado = await db.Faturamentos
            .Include(x => x.Glosas)
            .Include(x => x.ContasReceber)
            .SingleAsync(x => x.Id == faturamentoId);
        var contaAtualizada = faturamentoAtualizado.ContasReceber.Single(x => x.Id == contaId);

        Assert.Equal(300m, faturamentoAtualizado.ValorGlosado);
        Assert.Equal(700m, faturamentoAtualizado.ValorReconhecido);
        Assert.Equal("Divergencia revisada", faturamentoAtualizado.Glosas.Single().DescricaoMotivo);
        Assert.Equal(700m, contaAtualizada.ValorAjustado);
        Assert.Equal(700m, contaAtualizada.SaldoAberto);
    }

    [Fact]
    public async Task CriarEAtualizarAtendimento_ComCodigoCbhpmSemPontuacao_UsaValorOficialDoBackend()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var seed = await SeedFinanceiroAsync(factory);
        var codigoOficial = $"1.01.01.{Random.Shared.Next(10, 99)}-{Random.Shared.Next(0, 9)}";
        var codigoNormalizado = new string(codigoOficial.Where(char.IsDigit).ToArray());

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CbhpmGeral.Add(new CbhpmGeral
            {
                Codigo = codigoOficial,
                Procedimento = "Procedimento oficial CBHPM",
                Porte = "2B",
                ValorReferencia = 123.45m
            });
            await db.SaveChangesAsync();
        }

        var createResponse = await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", new
        {
            pacienteId = seed.PacienteId,
            dataProcedimento = new DateTime(2026, 7, 10),
            convenioId = seed.ConvenioId,
            medicoResponsavelId = seed.MedicoId,
            status = AtendimentoCirurgicoStatus.Planejado,
            procedimentos = new[]
            {
                new
                {
                    cbhpmCodigo = codigoNormalizado,
                    descricao = "Descricao manipulada pelo cliente",
                    quantidade = 1m,
                    pesoPercentual = 100m
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var createJson = await ReadJsonAsync(createResponse);
        var atendimentoId = createJson.RootElement.GetProperty("id").GetInt32();
        AssertProcedimentoOficial(createJson.RootElement.GetProperty("procedimentos")[0], codigoNormalizado);

        var updateResponse = await client.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{atendimentoId}", new
        {
            id = atendimentoId,
            dataProcedimento = new DateTime(2026, 7, 11),
            convenioId = seed.ConvenioId,
            medicoResponsavelId = seed.MedicoId,
            status = AtendimentoCirurgicoStatus.Realizado,
            procedimentos = new[]
            {
                new
                {
                    cbhpmCodigo = codigoNormalizado,
                    descricao = "Outra descricao manipulada",
                    quantidade = 1m,
                    pesoPercentual = 100m
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updateJson = await ReadJsonAsync(updateResponse);
        AssertProcedimentoOficial(updateJson.RootElement.GetProperty("procedimentos")[0], codigoNormalizado);
    }

    [Fact]
    public async Task CriarAtendimento_ComCatalogosManuaisNaSegundaClinica_AtribuiClinicaAtual()
    {
        using var factory = new HemodinksApiFactory();
        var beta = await SeedClinicaBetaAsync(factory);
        int pacienteId;
        int medicoId;

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            medicoId = await db.Users
                .Where(x => x.ClinicaId == beta.Id && x.PerfilId == Perfil.MedicosId)
                .Select(x => x.Id)
                .SingleAsync();
            var pacienteUser = new User
            {
                ClinicaId = beta.Id,
                Nome = "Paciente Financeiro Beta",
                Telefone = "+5511888888877",
                Email = $"paciente-beta-{Guid.NewGuid():N}@teste.local",
                Senha = "hash",
                PerfilId = Perfil.PacientesId,
                Ativo = true,
                PrecisaTrocarSenha = false
            };
            var paciente = new Paciente
            {
                ClinicaId = beta.Id,
                User = pacienteUser,
                NomePaciente = pacienteUser.Nome
            };
            db.Pacientes.Add(paciente);
            await db.SaveChangesAsync();
            pacienteId = paciente.Id;
        }

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        var suffix = Guid.NewGuid().ToString("N");
        var hospitalNome = $"Hospital manual beta {suffix}";
        var convenioNome = $"Convenio manual beta {suffix}";
        var opmeNome = $"OPME manual beta {suffix}";

        var response = await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", new
        {
            pacienteId,
            dataProcedimento = new DateTime(2026, 8, 10),
            hospital = hospitalNome,
            convenio = convenioNome,
            opmeFornecedor = opmeNome,
            medicoResponsavelId = medicoId,
            status = "Planejado",
            procedimentos = new[]
            {
                new
                {
                    cbhpmCodigo = "20202020",
                    descricao = "Cirurgia manual",
                    cbhpmPorte = "3C",
                    quantidade = 1m,
                    pesoPercentual = 100m
                }
            }
        });

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Esperado 201, recebido {(int)response.StatusCode}: {responseContent}");
        using var verificationScope = factory.Services.CreateScope();
        verificationScope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(beta.Id, (await verificationDb.Hospitais.SingleAsync(x => x.Nome == hospitalNome)).ClinicaId);
        Assert.Equal(beta.Id, (await verificationDb.Convenios.SingleAsync(x => x.DescricaoConvenio == convenioNome)).ClinicaId);
        Assert.Equal(beta.Id, (await verificationDb.OPME.SingleAsync(x => x.Fornecedor == opmeNome)).ClinicaId);
    }

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
            opmeFornecedorId = seed.OpmeFornecedorId,
            medicoResponsavelId = seed.MedicoId,
            diagnostico = "Diagnostico inicial",
            tratamentoMedico = "Procedimento cirurgico",
            numeroAutorizacao = "AUT-1",
            status = "Realizado",
            procedimentos = new[] { new { cbhpmCodigo = seed.CbhpmCodigo, descricao = "Procedimento teste", quantidade = 1m, pesoPercentual = 100m } }
        });
        Assert.Equal(HttpStatusCode.Created, atendimentoResponse.StatusCode);
        using var atendimentoJson = await ReadJsonAsync(atendimentoResponse);
        var atendimentoId = atendimentoJson.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(seed.OpmeFornecedorId, atendimentoJson.RootElement.GetProperty("opmeFornecedorId").GetInt32());
        Assert.Equal(seed.OpmeFornecedor, atendimentoJson.RootElement.GetProperty("opmeFornecedor").GetString());
        var atendimentoManualResponse = await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", new
        {
            pacienteId = seed.PacienteId,
            dataProcedimento = new DateTime(2026, 8, 10),
            hospital = "Hospital manual atendimento",
            convenio = "Convenio manual atendimento",
            opmeFornecedor = "OPME manual atendimento",
            medicoResponsavelId = seed.MedicoId,
            valorGlosa = 125m,
            motivoGlosa = "Divergencia contratual",
            status = "Planejado",
            procedimentos = new[] { new { descricao = "Procedimento manual", quantidade = 1m, pesoPercentual = 100m } }
        });
        Assert.Equal(HttpStatusCode.Created, atendimentoManualResponse.StatusCode);
        using (var atendimentoManualJson = await ReadJsonAsync(atendimentoManualResponse))
        {
            Assert.Equal(125m, atendimentoManualJson.RootElement.GetProperty("valorGlosa").GetDecimal());
            Assert.Equal("Divergencia contratual", atendimentoManualJson.RootElement.GetProperty("motivoGlosa").GetString());
        }
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull(await context.Hospitais.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.Nome == "Hospital manual atendimento"));
            Assert.NotNull(await context.Convenios.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.DescricaoConvenio == "Convenio manual atendimento"));
            Assert.NotNull(await context.OPME.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.Fornecedor == "OPME manual atendimento"));
        }
        var segundoAtendimentoResponse = await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", new
        {
            pacienteId = seed.PacienteId, dataProcedimento = new DateTime(2026, 9, 10), convenioId = seed.ConvenioId,
            medicoResponsavelId = seed.MedicoId, status = AtendimentoCirurgicoStatus.Planejado,
            procedimentos = new[] { new { cbhpmCodigo = seed.CbhpmCodigo, descricao = "Segundo procedimento", quantidade = 1m, pesoPercentual = 100m } }
        });
        Assert.Equal(HttpStatusCode.Created, segundoAtendimentoResponse.StatusCode);

        var detalheAtendimento = await client.GetAsync($"/api/atendimentos-cirurgicos/{atendimentoId}");
        Assert.Equal(HttpStatusCode.OK, detalheAtendimento.StatusCode);
        using (var detalheJson = await ReadJsonAsync(detalheAtendimento))
        {
            Assert.Equal(seed.OpmeFornecedorId, detalheJson.RootElement.GetProperty("opmeFornecedorId").GetInt32());
            Assert.Equal(seed.OpmeFornecedor, detalheJson.RootElement.GetProperty("opmeFornecedor").GetString());
        }
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
        var faturamentoItemId = faturamentoJson.RootElement.GetProperty("itens")[0].GetProperty("id").GetInt32();

        var itemResponse = await client.PutAsJsonAsync($"/api/faturamentos/{faturamentoId}/itens/{faturamentoItemId}", new
        {
            faturamentoId, itemId = faturamentoItemId, codigo = seed.CbhpmCodigo, descricao = "Procedimento revisado",
            quantidade = 1m, pesoPercentual = 100m, valorUnitario = 950m, rowVersion = faturamentoVersion
        });
        Assert.Equal(HttpStatusCode.OK, itemResponse.StatusCode);
        using var itemJson = await ReadJsonAsync(itemResponse);
        Assert.Equal(950m, itemJson.RootElement.GetProperty("valorApresentado").GetDecimal());
        faturamentoVersion = itemJson.RootElement.GetProperty("rowVersion").GetBytesFromBase64();

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
        using var readyJson = await ReadJsonAsync(statusResponse);
        var readyVersion = readyJson.RootElement.GetProperty("rowVersion").GetBytesFromBase64();
        var sendResponse = await client.PutAsJsonAsync($"/api/faturamentos/{faturamentoId}/status", new
        {
            id = faturamentoId, status = FaturamentoStatus.Enviado, rowVersion = readyVersion
        });
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);
        using var sentJson = await ReadJsonAsync(sendResponse);
        Assert.NotEqual(JsonValueKind.Null, sentJson.RootElement.GetProperty("dataEnvio").ValueKind);

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
        var duplicateAccountResponse = await client.PostAsJsonAsync($"/api/faturamentos/{faturamentoId}/contas-receber", new
        {
            faturamentoId, numeroDocumento = "TIT-1", descricao = "Honorarios", dataEmissao = new DateTime(2026, 7, 12),
            dataVencimento = new DateTime(2026, 8, 12)
        });
        Assert.Equal(HttpStatusCode.OK, duplicateAccountResponse.StatusCode);
        using var duplicateJson = await ReadJsonAsync(duplicateAccountResponse);
        Assert.Equal(contaId, duplicateJson.RootElement.GetProperty("id").GetInt32());

        var recebimentoResponse = await client.PostAsJsonAsync($"/api/financeiro/contas-receber/{contaId}/recebimentos", new
        {
            contaReceberId = contaId, dataRecebimento = new DateTime(2026, 8, 1), valorRecebido = saldo,
            formaRecebimento = FormaRecebimento.Pix, referenciaBancaria = "PIX-1", observacao = "Recebido",
            usuarioCadastroId = 0, rowVersion = contaVersion
        });
        Assert.Equal(HttpStatusCode.OK, recebimentoResponse.StatusCode);
        using var recebidoJson = await ReadJsonAsync(recebimentoResponse);
        var recebimentoId = recebidoJson.RootElement.GetProperty("recebimentos")[0].GetProperty("id").GetInt32();
        var patientSummary = await client.GetAsync($"/api/pacientes/{seed.PacienteId}/resumo-financeiro");
        Assert.Equal(HttpStatusCode.OK, patientSummary.StatusCode);
        using var patientSummaryJson = await ReadJsonAsync(patientSummary);
        Assert.Equal(saldo, patientSummaryJson.RootElement.GetProperty("valorRecebido").GetDecimal());
        Assert.Equal(0m, patientSummaryJson.RootElement.GetProperty("saldoAberto").GetDecimal());
        var paidVersion = recebidoJson.RootElement.GetProperty("rowVersion").GetBytesFromBase64();
        var aboveBalance = await client.PostAsJsonAsync($"/api/financeiro/contas-receber/{contaId}/recebimentos", new
        {
            contaReceberId = contaId, dataRecebimento = new DateTime(2026, 8, 2), valorRecebido = 1m,
            formaRecebimento = FormaRecebimento.Pix, usuarioCadastroId = 0, rowVersion = paidVersion
        });
        Assert.Equal(HttpStatusCode.BadRequest, aboveBalance.StatusCode);

        using var invalidMultipart = new MultipartFormDataContent();
        invalidMultipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("imagem")) { Headers = { ContentType = new("image/png") } }, "arquivo", "comprovante.png");
        var invalidUploadResponse = await client.PostAsync($"/api/financeiro/contas-receber/recebimentos/{recebimentoId}/comprovante", invalidMultipart);
        Assert.Equal(HttpStatusCode.BadRequest, invalidUploadResponse.StatusCode);

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("comprovante")) { Headers = { ContentType = new("application/pdf") } }, "arquivo", "comprovante.pdf");
        var uploadResponse = await client.PostAsync($"/api/financeiro/contas-receber/recebimentos/{recebimentoId}/comprovante", multipart);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.Equal("comprovante.pdf", fileStorage.LastSavedName);
        var downloadResponse = await client.GetAsync($"/api/financeiro/contas-receber/recebimentos/{recebimentoId}/comprovante");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("application/pdf", downloadResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"comprovante-{recebimentoId}.pdf", downloadResponse.Content.Headers.ContentDisposition?.FileNameStar);

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
        using var invalidBody = new StringContent("{\"pacienteId\":1,\"status\":\"StatusInexistente\"}", Encoding.UTF8, "application/json");
        var invalidResponse = await client.PostAsync("/api/atendimentos-cirurgicos/", invalidBody);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        var invalidMessage = await invalidResponse.Content.ReadAsStringAsync();
        Assert.Contains("formato invalido", invalidMessage);
        Assert.DoesNotContain("stack trace", invalidMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Medico_CannotUpdateOrDeleteAtendimentoAssignedToAnotherDoctor()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        int atendimentoId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var atendimento = new AtendimentoCirurgico
            {
                ClinicaId = Clinica.DefaultId,
                PacienteId = seed.PacienteId,
                DataProcedimento = new DateTime(2026, 10, 10),
                MedicoResponsavelId = seed.MedicoId,
                Status = AtendimentoCirurgicoStatus.Planejado
            };
            db.AtendimentosCirurgicos.Add(atendimento);
            await db.SaveChangesAsync();
            atendimentoId = atendimento.Id;
        }

        using var client = factory.CreateClient();
        await AuthenticateAsync(
            client,
            Clinica.DefaultSlug,
            "maria.silva@email.com",
            TestPasswords.Valid);

        var updateResponse = await client.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{atendimentoId}", new
        {
            id = atendimentoId,
            dataProcedimento = new DateTime(2026, 10, 11),
            medicoResponsavelId = seed.MedicoId,
            status = AtendimentoCirurgicoStatus.Realizado,
            procedimentos = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/atendimentos-cirurgicos/{atendimentoId}")).StatusCode);
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
        var opme = new Opme { ClinicaId = Clinica.DefaultId, Fornecedor = $"Fornecedor OPME {suffix}" };
        db.Users.AddRange(medico, pacienteUser); db.Convenios.Add(convenio); db.OPME.Add(opme); await db.SaveChangesAsync();
        var paciente = new Paciente { ClinicaId = Clinica.DefaultId, UserId = pacienteUser.Id, NomePaciente = pacienteUser.Nome };
        db.Pacientes.Add(paciente); await db.SaveChangesAsync();
        const string code = "99999999";
        if (!await db.CbhpmGeral.AnyAsync(x => x.Codigo == code)) db.CbhpmGeral.Add(new CbhpmGeral { Codigo = code, Procedimento = "Procedimento financeiro", ValorReferencia = 1000m });
        await db.SaveChangesAsync();
        return new(paciente.Id, medico.Id, convenio.IdConvenio, opme.IdFornecedor, opme.Fornecedor, code);
    }

    private sealed record FinanceiroSeed(int PacienteId, int MedicoId, int ConvenioId,
        int OpmeFornecedorId, string OpmeFornecedor, string CbhpmCodigo);

    private static void AssertProcedimentoOficial(JsonElement procedimento, string codigoNormalizado)
    {
        Assert.Equal(codigoNormalizado, procedimento.GetProperty("cbhpmCodigo").GetString());
        Assert.Equal("Procedimento oficial CBHPM", procedimento.GetProperty("descricao").GetString());
        Assert.Equal("2B", procedimento.GetProperty("cbhpmPorte").GetString());
        Assert.Equal(123.45m, procedimento.GetProperty("valorReferencia").GetDecimal());
    }

    private sealed class TestingFinancialFileStorage : IPatientFileStorage
    {
        private byte[] _content = [];
        public string? LastSavedName { get; private set; }
        public bool DeleteCalled { get; private set; }
        public async Task<StoredPatientFile> SaveAsync(UploadedFile file, CancellationToken cancellationToken)
        {
            LastSavedName = file.FileName; await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken); _content = stream.ToArray();
            return new(file.FileName, file.ContentType, file.Length, "https://storage.test/comprovante.pdf");
        }
        public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, CancellationToken cancellationToken) =>
            Task.FromResult<StoredPatientFileContent?>(new(new MemoryStream(_content)));
        public Task DeleteAsync(string? fileUrl, CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }
    }
}
