using System.Net;
using System.Text;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

// #226: novos uploads em clinicas/{clinicaId}/pacientes/..., legado sem migração, recusa entre clínicas.
public sealed class PatientFileBlobPathTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_PrefixesClinicAndKeepsPatientLayout()
    {
        var path = PatientFileBlobPath.Build(7, ".pdf", Now);

        Assert.Matches(@"^clinicas/7/pacientes/2026/11/[0-9a-f]{32}\.pdf$", path);
        Assert.Matches(@"^pacientes/2026/11/[0-9a-f]{32}\.pdf$", PatientFileBlobPath.BuildLegacy(".pdf", Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_WithInvalidClinic_FailsClosed(int clinicaId)
    {
        Assert.Throws<InvalidOperationException>(() => PatientFileBlobPath.Build(clinicaId, ".pdf", Now));
    }

    // Recebe o nome de blob JÁ RESOLVIDO pelo storage; não interpreta URL nem decodifica de novo.
    [Theory]
    [InlineData("pacientes/2026/09/a.pdf", null)]
    [InlineData("clinicas/7/pacientes/2026/11/a.pdf", 7)]
    [InlineData("CLINICAS/12/Pacientes/2026/11/a.pdf", 12)]
    [InlineData("clinicas/abc/pacientes/2026/11/a.pdf", 0)]
    [InlineData("clinicas/0/pacientes/2026/11/a.pdf", 0)]
    [InlineData("clinicas/2/pacientes/2026/11/g.pdf/clinicas/1/pacientes/../../..", 0)]
    [InlineData("pacientes/../clinicas/2/pacientes/2026/11/g.pdf", 0)]
    [InlineData("pacientes/2026/09/a.pdf%2Fclinicas%2F1%2Fpacientes%2Fx", 0)]
    [InlineData(@"clinicas\2\pacientes/x.pdf", 0)]
    [InlineData("", null)]
    public void GetOwnerClinicaId_ParsesPrefixAndTreatsMalformedAsNobody(string blobName, int? expected)
    {
        Assert.Equal(expected, PatientFileBlobPath.GetOwnerClinicaId(blobName));
    }

    [Theory]
    [InlineData("clinicas/7/pacientes/2026/11/a.pdf", 7, true)]
    [InlineData("clinicas/7/pacientes/2026/11/a.pdf", 8, false)]
    [InlineData("pacientes/2026/09/a.pdf", 8, true)]
    [InlineData("clinicas/abc/pacientes/2026/11/a.pdf", 1, false)]
    [InlineData("clinicas/7/pacientes/2026/11/a.pdf", 0, false)]
    [InlineData("pacientes/2026/09/a.pdf", 0, false)]
    [InlineData("pacientes/../clinicas/2/pacientes/2026/11/g.pdf", 1, false)]
    public void BelongsTo_IsFailClosed(string path, int clinicaId, bool expected)
    {
        Assert.Equal(expected, PatientFileBlobPath.BelongsTo(path, clinicaId));
    }
}

public sealed class LocalDiskPatientFileClinicPrefixTests : IDisposable
{
    private static readonly byte[] Pdf = "%PDF-1.7\nconteudo\n%%EOF"u8.ToArray();
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "hemodinks-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task NewUpload_IsPrefixedAndOnlyOwnerClinicCanReadOrDelete()
    {
        var storage = CreateStorage();
        var stored = await storage.SaveAsync(PdfFile(), 7, CancellationToken.None);

        Assert.Contains("/patient-files/clinicas/7/pacientes/", stored.Url);
        using (var own = await storage.GetAsync(stored.Url, 7, CancellationToken.None))
            Assert.NotNull(own);

        // Clínica B com registro apontando para o arquivo da Clínica A: 404 genérico e nada é apagado.
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => storage.GetAsync(stored.Url, 8, CancellationToken.None));
        Assert.Equal("Arquivo nao encontrado.", ex.Message);
        await storage.DeleteAsync(stored.Url, 8, CancellationToken.None);
        Assert.True(File.Exists(LocalPath(stored.Url)));

        await storage.DeleteAsync(stored.Url, 7, CancellationToken.None);
        Assert.False(File.Exists(LocalPath(stored.Url)));
    }

    [Fact]
    public async Task LegacyFile_WithoutPrefix_StillReadable()
    {
        var storage = CreateStorage();
        var legacyUrl = "http://localhost:5000/storage/patient-files/pacientes/2026/09/legado.pdf";
        var legacyPath = LocalPath(legacyUrl);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        await File.WriteAllBytesAsync(legacyPath, Pdf);

        using var content = await storage.GetAsync(legacyUrl, 5, CancellationToken.None);

        Assert.NotNull(content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Save_WithInvalidClinic_FailsClosedWithoutWriting(int clinicaId)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateStorage().SaveAsync(PdfFile(), clinicaId, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(_rootPath, "patient-files")));
    }

    private LocalDiskPatientFileStorage CreateStorage() => new(
        Options.Create(new PatientFileStorageOptions { MaxBytes = 1024 * 1024 }),
        Options.Create(new LocalStorageOptions { RootPath = _rootPath, RequestPath = "/storage", PublicBaseUrl = "http://localhost:5000" }),
        NullLogger<LocalDiskPatientFileStorage>.Instance);

    private static UploadedFile PdfFile() =>
        new("laudo.pdf", "application/pdf", Pdf.LongLength, () => new MemoryStream(Pdf, writable: false));

    private string LocalPath(string url) => Path.Combine(_rootPath, "patient-files",
        new Uri(url).AbsolutePath["/storage/patient-files/".Length..].Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        if (Directory.Exists(_rootPath)) Directory.Delete(_rootPath, recursive: true);
    }
}

public sealed class PatientFileClinicPrefixEndpointTests : ApiEndpointTestBase, IDisposable
{
    private static readonly byte[] Pdf = "%PDF-1.7\nconteudo clinico\n%%EOF"u8.ToArray();
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "hemodinks-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Upload_IsPrefixedWithClinic_AndLegacyAndNewFilesDownload()
    {
        using var factory = CreateFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Pdf) { Headers = { ContentType = new("application/pdf") } }, "file", "laudo.pdf");
        using var upload = await client.PostAsync($"/api/pacientes/{seed.PacienteId}/arquivos", form);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());

        var stored = await ReadArquivosAsync(factory, Clinica.DefaultId, seed.PacienteId);
        var novo = Assert.Single(stored);
        Assert.Contains($"/clinicas/{Clinica.DefaultId}/pacientes/", novo.Url);
        using (var download = await client.GetAsync($"/api/pacientes/{seed.PacienteId}/arquivos/{novo.Id}/download"))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
        }

        var legacyId = await SeedArquivoAsync(factory, Clinica.DefaultId, seed.PacienteId,
            "http://localhost:5000/storage/patient-files/pacientes/2026/09/legado.pdf", writeFile: true);
        using var legacy = await client.GetAsync($"/api/pacientes/{seed.PacienteId}/arquivos/{legacyId}/download");
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal(Pdf, await legacy.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ClinicB_RecordPointingToClinicAFile_Returns404WithoutContent()
    {
        using var factory = CreateFactory();
        var seed = await SeedFinanceiroAsync(factory);
        var beta = await SeedClinicaBetaAsync(factory);
        var clinicAUrl = $"http://localhost:5000/storage/patient-files/clinicas/{Clinica.DefaultId}/pacientes/2026/11/a.pdf";
        await SeedArquivoAsync(factory, Clinica.DefaultId, seed.PacienteId, clinicAUrl, writeFile: true);

        var betaPatientId = await SeedPacienteAsync(factory, beta.Id);
        var tamperedId = await SeedArquivoAsync(factory, beta.Id, betaPatientId, clinicAUrl, writeFile: false);

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        using var response = await client.GetAsync($"/api/pacientes/{betaPatientId}/arquivos/{tamperedId}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("conteudo clinico", Encoding.UTF8.GetString(body));
        Assert.True(File.Exists(LocalPath(clinicAUrl)));
    }

    private HemodinksApiFactory CreateFactory() => new(services =>
    {
        services.RemoveAll<IPatientFileStorage>();
        services.AddSingleton<IPatientFileStorage>(new LocalDiskPatientFileStorage(
            Options.Create(new PatientFileStorageOptions { MaxBytes = 1024 * 1024 }),
            Options.Create(new LocalStorageOptions { RootPath = _rootPath, RequestPath = "/storage", PublicBaseUrl = "http://localhost:5000" }),
            NullLogger<LocalDiskPatientFileStorage>.Instance));
    });

    private static async Task<List<PacienteArquivo>> ReadArquivosAsync(HemodinksApiFactory factory, int clinicaId, int pacienteId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(clinicaId, Clinica.DefaultSlug);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PacienteArquivos.AsNoTracking()
            .Where(x => x.PacienteId == pacienteId).ToListAsync();
    }

    private async Task<int> SeedArquivoAsync(HemodinksApiFactory factory, int clinicaId, int pacienteId, string url, bool writeFile)
    {
        if (writeFile)
        {
            var path = LocalPath(url);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, Pdf);
        }

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var arquivo = new PacienteArquivo
        {
            ClinicaId = clinicaId, PacienteId = pacienteId, NomeOriginal = "laudo.pdf",
            ContentType = "application/pdf", TamanhoBytes = Pdf.LongLength, Url = url
        };
        db.PacienteArquivos.Add(arquivo);
        await db.SaveChangesAsync();
        return arquivo.Id;
    }

    private static async Task<int> SeedPacienteAsync(HemodinksApiFactory factory, int clinicaId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User
        {
            ClinicaId = clinicaId, Nome = "Paciente Beta", Telefone = "+5511955555555",
            Email = $"beta-{suffix}@teste.local", Senha = "hash", PerfilId = Perfil.PacientesId, Ativo = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var paciente = new Paciente { ClinicaId = clinicaId, UserId = user.Id, NomePaciente = user.Nome };
        db.Pacientes.Add(paciente);
        await db.SaveChangesAsync();
        return paciente.Id;
    }

    private string LocalPath(string url) => Path.Combine(_rootPath, "patient-files",
        new Uri(url).AbsolutePath["/storage/patient-files/".Length..].Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        if (Directory.Exists(_rootPath)) Directory.Delete(_rootPath, recursive: true);
    }
}

public sealed class StorageFunctionClinicHeaderTests
{
    [Fact]
    public void ParseClinicaId_AcceptsPositiveIdAndMissingHeader()
    {
        Assert.Equal(7, StorageFunctionHeaders.ParseClinicaId(["7"]));
        Assert.Null(StorageFunctionHeaders.ParseClinicaId(null));
        Assert.Null(StorageFunctionHeaders.ParseClinicaId([]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(" 7")]
    public void ParseClinicaId_RejectsInvalidValues(string value)
    {
        Assert.Throws<InvalidOperationException>(() => StorageFunctionHeaders.ParseClinicaId([value]));
    }

    [Fact]
    public void ParseClinicaId_RejectsRepeatedHeader()
    {
        Assert.Throws<InvalidOperationException>(() => StorageFunctionHeaders.ParseClinicaId(["7", "8"]));
    }

    [Fact]
    public async Task FunctionBackedStorage_SendsClinicFromRecordAsHeader()
    {
        var handler = new CapturingHandler();
        var client = new StorageFunctionClient(new SingleClientFactory(handler),
            Options.Create(new StorageFunctionOptions { BaseUrl = "https://functions.example/api/", FunctionKey = "k" }));
        var storage = new FunctionBackedPatientFileStorage(client,
            new AzureBlobPatientFileStorage(Options.Create(new PatientFileStorageOptions()), NullLogger<AzureBlobPatientFileStorage>.Instance),
            Options.Create(new PatientFileStorageOptions { MaxBytes = 1024 * 1024 }));
        var pdf = "%PDF-1.7\nx\n%%EOF"u8.ToArray();

        await storage.SaveAsync(new UploadedFile("laudo.pdf", "application/pdf", pdf.LongLength, () => new MemoryStream(pdf)), 12, CancellationToken.None);

        Assert.Equal(["12"], handler.ClinicaIds);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync(new UploadedFile("laudo.pdf", "application/pdf", pdf.LongLength, () => new MemoryStream(pdf)), 0, CancellationToken.None));
        Assert.Single(handler.ClinicaIds);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> ClinicaIds { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ClinicaIds.AddRange(request.Headers.TryGetValues(StorageFunctionHeaders.ClinicaId, out var v) ? v : []);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"originalName\":\"laudo.pdf\",\"contentType\":\"application/pdf\",\"sizeBytes\":1,\"url\":\"clinicas/12/pacientes/2026/11/x.pdf\"}",
                    Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

public sealed class AzureBlobPatientFileClinicPrefixTests
{
    private const string PublicBase = "https://stgexemplo.blob.core.windows.net/patient-files";

    private static AzureBlobPatientFileStorage CreateStorage() => new(
        Options.Create(new PatientFileStorageOptions { ContainerName = "patient-files", PublicBaseUrl = PublicBase }),
        NullLogger<AzureBlobPatientFileStorage>.Instance);

    // Bypass apontado na revisão: URL "legada" com ? ou # que o resolver transforma em blob de outra clínica.
    [Theory]
    [InlineData(PublicBase + "/pacientes/x.pdf?/clinicas/2/pacientes/2026/11/g.pdf")]
    [InlineData(PublicBase + "/pacientes/x.pdf#/clinicas/2/pacientes/2026/11/g.pdf")]
    [InlineData(PublicBase + "/clinicas/2/pacientes/2026/11/g.pdf")]
    [InlineData("clinicas/2/pacientes/2026/11/g.pdf")]
    public async Task GetAsync_BlobOfAnotherClinic_Returns404BeforeTouchingStorage(string url)
    {
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateStorage().GetAsync(url, 1, CancellationToken.None));
        Assert.Equal("Arquivo nao encontrado.", ex.Message);
    }

    [Theory]
    [InlineData(PublicBase + "/clinicas/1/pacientes/2026/11/g.pdf")]
    [InlineData(PublicBase + "/pacientes/2026/09/legado.pdf")]
    public async Task GetAsync_OwnOrLegacyBlob_PassesOwnershipAndReachesStorage(string url)
    {
        // Sem connection string configurada: chegar aqui prova que a verificação de dono foi aprovada.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateStorage().GetAsync(url, 1, CancellationToken.None));
        Assert.Contains("ConnectionString", ex.Message);
    }
}
