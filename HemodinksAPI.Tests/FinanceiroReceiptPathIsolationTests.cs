using System.Net;
using System.Net.Http.Json;
using System.Text;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HemodinksAPI.Tests;

// #268: o caminho do comprovante nunca vem do payload; so o upload grava a URL gerada pelo storage.
public sealed class FinanceiroReceiptPathIsolationTests : ApiEndpointTestBase
{
    private const string ClinicALegacyPath = "https://storage.test/patient-files/legado-clinica-a.pdf";

    [Fact]
    public async Task RegistrarRecebimento_IgnoraCaminhoDeStorageDoPayload_ENaoBaixaNemExcluiArquivoDeOutraClinica()
    {
        var storage = new RecordingStorage();
        using var factory = CreateFactory(storage);
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        var contaBetaId = await SeedContaAsync(factory, beta.Id, receiptPath: null);
        await AuthenticateAsync(client, beta.Slug);

        var rowVersion = await GetRowVersionAsync(client, contaBetaId);
        var response = await client.PostAsJsonAsync($"/api/financeiro/contas-receber/{contaBetaId}/recebimentos", new
        {
            dataRecebimento = new DateTime(2026, 8, 1), valorRecebido = 10m, formaRecebimento = FormaRecebimento.Pix,
            documentoComprovante = ClinicALegacyPath, rowVersion
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var receipt = json.RootElement.GetProperty("recebimentos")[0];
        var receiptId = receipt.GetProperty("id").GetInt32();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, receipt.GetProperty("documentoComprovante").ValueKind);
        Assert.Null(await ReadStoredPathAsync(factory, receiptId));

        var download = await client.GetAsync($"/api/financeiro/contas-receber/recebimentos/{receiptId}/comprovante");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.DoesNotContain("legado-clinica-a", await download.Content.ReadAsStringAsync());

        using var upload = PdfUpload();
        var uploadResponse = await client.PostAsync($"/api/financeiro/contas-receber/recebimentos/{receiptId}/comprovante", upload);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

        Assert.DoesNotContain(ClinicALegacyPath, storage.Reads);
        Assert.DoesNotContain(ClinicALegacyPath, storage.Deletes);
        Assert.Empty(storage.Deletes);
        Assert.Equal([beta.Id], storage.SavedForClinics);
    }

    [Fact]
    public async Task ComprovanteDaClinicaA_ClinicaBNaoBaixaNemSubstitui_E404SemRevelarExistencia()
    {
        var storage = new RecordingStorage();
        using var factory = CreateFactory(storage);
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await SeedContaAsync(factory, Clinica.DefaultId, receiptPath: ClinicALegacyPath);
        var receiptAId = await ReceiptIdByPathAsync(factory, ClinicALegacyPath);
        await AuthenticateAsync(client, beta.Slug);

        var download = await client.GetAsync($"/api/financeiro/contas-receber/recebimentos/{receiptAId}/comprovante");
        using var upload = PdfUpload();
        var replace = await client.PostAsync($"/api/financeiro/contas-receber/recebimentos/{receiptAId}/comprovante", upload);
        var missing = await client.GetAsync("/api/financeiro/contas-receber/recebimentos/999999/comprovante");

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, replace.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await download.Content.ReadAsStringAsync());
        Assert.Empty(storage.Reads);
        Assert.Empty(storage.Deletes);
        Assert.Empty(storage.SavedForClinics);
        Assert.Equal(ClinicALegacyPath, await ReadStoredPathAsync(factory, receiptAId));
    }

    private static HemodinksApiFactory CreateFactory(RecordingStorage storage) => new(services =>
    {
        services.RemoveAll<IPatientFileStorage>();
        services.AddSingleton<IPatientFileStorage>(storage);
    });

    private static MultipartFormDataContent PdfUpload()
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("comprovante")) { Headers = { ContentType = new("application/pdf") } },
            "arquivo", "comprovante.pdf");
        return multipart;
    }

    private static async Task<byte[]> GetRowVersionAsync(HttpClient client, int contaId)
    {
        var response = await client.GetAsync($"/api/financeiro/contas-receber/{contaId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("rowVersion").GetBytesFromBase64();
    }

    private static async Task<int> SeedContaAsync(HemodinksApiFactory factory, int clinicId, string? receiptPath)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User { ClinicaId = clinicId, Nome = "Paciente Comprovante", Telefone = "+5511977777777",
            Email = $"paciente-{suffix}@teste.local", Senha = "hash", PerfilId = Perfil.PacientesId, Ativo = true, PrecisaTrocarSenha = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var patient = new Paciente { ClinicaId = clinicId, UserId = user.Id, NomePaciente = user.Nome };
        var attendance = new AtendimentoCirurgico { ClinicaId = clinicId, Paciente = patient, DataProcedimento = new DateTime(2026, 7, 1) };
        var billing = new Faturamento { ClinicaId = clinicId, AtendimentoCirurgico = attendance, Competencia = new DateTime(2026, 7, 1),
            ValorApresentado = 100m, ValorReconhecido = 100m };
        var account = new ContaReceber { ClinicaId = clinicId, Paciente = patient, Faturamento = billing, NumeroDocumento = $"TIT-{suffix[..8]}",
            Descricao = "Titulo comprovante", Competencia = billing.Competencia, DataEmissao = billing.Competencia,
            DataVencimento = new DateTime(2030, 1, 1), ValorOriginal = 100m, ValorAjustado = 100m, SaldoAberto = 100m,
            Status = ContaReceberStatus.Aberto, RowVersion = [1, 2, 3, 4, 5, 6, 7, 8] };
        if (receiptPath is not null)
        {
            account.Recebimentos.Add(new Recebimento { ClinicaId = clinicId, DataRecebimento = new DateTime(2026, 7, 2), ValorRecebido = 10m,
                FormaRecebimento = FormaRecebimento.Pix, DocumentoComprovante = receiptPath, UsuarioCadastroId = user.Id });
        }
        db.AddRange(patient, attendance, billing, account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<int> ReceiptIdByPathAsync(HemodinksApiFactory factory, string path)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Recebimentos.Where(x => x.DocumentoComprovante == path).Select(x => x.Id).SingleAsync();
    }

    private static async Task<string?> ReadStoredPathAsync(HemodinksApiFactory factory, int receiptId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Recebimentos.Where(x => x.Id == receiptId).Select(x => x.DocumentoComprovante).SingleAsync();
    }

    private sealed class RecordingStorage : IPatientFileStorage
    {
        public List<string?> Reads { get; } = [];
        public List<string?> Deletes { get; } = [];
        public List<int> SavedForClinics { get; } = [];

        public Task<StoredPatientFile> SaveAsync(UploadedFile file, int clinicaId, CancellationToken cancellationToken)
        {
            SavedForClinics.Add(clinicaId);
            return Task.FromResult(new StoredPatientFile(file.FileName, file.ContentType, file.Length,
                $"https://storage.test/patient-files/clinicas/{clinicaId}/comprovante.pdf"));
        }

        public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken)
        {
            Reads.Add(fileUrl);
            return Task.FromResult<StoredPatientFileContent?>(new(new MemoryStream(Encoding.UTF8.GetBytes("conteudo"))));
        }

        public Task DeleteAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken)
        {
            Deletes.Add(fileUrl);
            return Task.CompletedTask;
        }
    }
}
