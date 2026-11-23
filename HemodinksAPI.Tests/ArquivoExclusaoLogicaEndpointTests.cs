using System.Net;
using System.Text;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HemodinksAPI.Tests;

// Excluir arquivo de paciente ou de atendimento e exclusao logica: some da aplicacao, mas linha e storage ficam.
public sealed class ArquivoExclusaoLogicaEndpointTests : ApiEndpointTestBase
{
    [Fact]
    public async Task ArquivoDePaciente_ExcluidoSomeDaAplicacao_MasLinhaEStorageSaoPreservados()
    {
        var storage = new RecordingStorage();
        using var factory = CreateFactory(storage);
        var seed = await SeedFinanceiroAsync(factory);
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin);

        using var upload = Pdf("file", "laudo-preservado.pdf");
        var created = await admin.PostAsync($"/api/pacientes/{seed.PacienteId}/arquivos", upload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = await ReadJsonAsync(created);
        var fileId = createdJson.RootElement.GetProperty("id").GetInt32();
        Assert.Contains("laudo-preservado.pdf", await admin.GetStringAsync($"/api/pacientes/{seed.PacienteId}"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/pacientes/{seed.PacienteId}/arquivos/{fileId}")).StatusCode);

        var detail = await admin.GetStringAsync($"/api/pacientes/{seed.PacienteId}");
        Assert.DoesNotContain("laudo-preservado.pdf", detail);
        Assert.Contains("\"arquivosCount\":0", detail);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/pacientes/{seed.PacienteId}/arquivos/{fileId}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.DeleteAsync($"/api/pacientes/{seed.PacienteId}/arquivos/{fileId}")).StatusCode);
        Assert.Empty(storage.Deletes);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PacienteArquivos.SingleAsync(a => a.Id == fileId);
        Assert.NotNull(stored.ExcluidoEm);
        Assert.NotNull(stored.ExcluidoPorUserId);
    }

    [Fact]
    public async Task ArquivoDeAtendimento_ExcluidoSomeDaAplicacao_MasLinhaEStorageSaoPreservados()
    {
        var storage = new RecordingStorage();
        using var factory = CreateFactory(storage);
        var seed = await SeedFinanceiroAsync(factory);
        int atendimentoId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var atendimento = new AtendimentoCirurgico
            {
                ClinicaId = Clinica.DefaultId, PacienteId = seed.PacienteId, MedicoResponsavelId = seed.MedicoId,
                DataProcedimento = new DateTime(2026, 10, 10)
            };
            db.AtendimentosCirurgicos.Add(atendimento);
            await db.SaveChangesAsync();
            atendimentoId = atendimento.Id;
        }
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin);

        using var upload = Pdf("arquivo", "laudo-cirurgico.pdf");
        var created = await admin.PostAsync($"/api/atendimentos-cirurgicos/{atendimentoId}/arquivos", upload);
        Assert.True(created.IsSuccessStatusCode, created.StatusCode.ToString());
        using var createdJson = await ReadJsonAsync(created);
        var fileId = createdJson.RootElement.GetProperty("id").GetInt32();
        Assert.Contains("laudo-cirurgico.pdf", await admin.GetStringAsync($"/api/atendimentos-cirurgicos/{atendimentoId}"));

        Assert.True((await admin.DeleteAsync($"/api/atendimentos-cirurgicos/{atendimentoId}/arquivos/{fileId}")).IsSuccessStatusCode);

        Assert.DoesNotContain("laudo-cirurgico.pdf", await admin.GetStringAsync($"/api/atendimentos-cirurgicos/{atendimentoId}"));
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/atendimentos-cirurgicos/{atendimentoId}/arquivos/{fileId}/download")).StatusCode);
        Assert.Empty(storage.Deletes);

        using var check = factory.Services.CreateScope();
        check.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var stored = await check.ServiceProvider.GetRequiredService<AppDbContext>().AtendimentoArquivos.SingleAsync(a => a.Id == fileId);
        Assert.NotNull(stored.ExcluidoEm);
    }

    [Fact]
    public async Task ClinicaB_NaoExcluiArquivoDePacienteDaClinicaA()
    {
        using var factory = CreateFactory(new RecordingStorage());
        var seed = await SeedFinanceiroAsync(factory);
        int fileId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var file = new PacienteArquivo
            {
                ClinicaId = Clinica.DefaultId, PacienteId = seed.PacienteId, NomeOriginal = "laudo-a.pdf",
                ContentType = "application/pdf", TamanhoBytes = 5, Url = "https://storage.test/clinicas/1/a.pdf"
            };
            db.PacienteArquivos.Add(file);
            await db.SaveChangesAsync();
            fileId = file.Id;
        }
        using var startup = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        using var betaAdmin = factory.CreateClient();
        await AuthenticateAsync(betaAdmin, beta.Slug);

        Assert.Equal(HttpStatusCode.NotFound,
            (await betaAdmin.DeleteAsync($"/api/pacientes/{seed.PacienteId}/arquivos/{fileId}")).StatusCode);

        using var check = factory.Services.CreateScope();
        check.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        Assert.Null((await check.ServiceProvider.GetRequiredService<AppDbContext>().PacienteArquivos.SingleAsync(a => a.Id == fileId)).ExcluidoEm);
    }

    private static MultipartFormDataContent Pdf(string field, string name)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 laudo")) { Headers = { ContentType = new("application/pdf") } }, field, name);
        return content;
    }

    private static HemodinksApiFactory CreateFactory(RecordingStorage storage) => new(services =>
    {
        services.RemoveAll<IPatientFileStorage>();
        services.AddSingleton<IPatientFileStorage>(storage);
    });

    private sealed class RecordingStorage : IPatientFileStorage
    {
        public List<string?> Deletes { get; } = [];

        public Task<StoredPatientFile> SaveAsync(UploadedFile file, int clinicaId, CancellationToken cancellationToken) =>
            Task.FromResult(new StoredPatientFile(file.FileName, file.ContentType, file.Length,
                $"https://storage.test/patient-files/clinicas/{clinicaId}/{Guid.NewGuid():N}.pdf"));

        public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredPatientFileContent?>(new(new MemoryStream([1])));

        public Task DeleteAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken)
        {
            Deletes.Add(fileUrl);
            return Task.CompletedTask;
        }
    }
}
