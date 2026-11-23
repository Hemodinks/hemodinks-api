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

// DELETE de atendimento cirurgico e cancelamento logico: nada e apagado (guarda do prontuario).
public sealed class AtendimentoCancelamentoEndpointTests : ApiEndpointTestBase
{
    private const string DoctorEmail = "maria.silva@email.com";

    [Fact]
    public async Task Cancelar_PreservaRegistroEArquivos_TiraDasListas_E_BloqueiaAlteracoes()
    {
        var storage = new RecordingStorage();
        using var factory = CreateFactory(storage);
        var seed = await SeedFinanceiroAsync(factory);
        var doctorId = await UserIdAsync(factory, DoctorEmail);
        var atendimentoId = await SeedAtendimentoAsync(factory, seed.PacienteId, doctorId, faturado: false);

        using var doctor = factory.CreateClient();
        await AuthenticateAsync(doctor, Clinica.DefaultSlug, DoctorEmail, TestPasswords.Valid);
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin);

        Assert.Contains($"\"id\":{atendimentoId},", await doctor.GetStringAsync("/api/atendimentos-cirurgicos/"));
        var cancel = await doctor.DeleteAsync($"/api/atendimentos-cirurgicos/{atendimentoId}?motivo=Registro%20duplicado");
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await doctor.DeleteAsync($"/api/atendimentos-cirurgicos/{atendimentoId}")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.AtendimentosCirurgicos.Include(a => a.Arquivos).Include(a => a.Procedimentos)
                .SingleAsync(a => a.Id == atendimentoId);
            Assert.Equal(AtendimentoCirurgicoStatus.Cancelado, stored.Status);
            Assert.NotNull(stored.CanceladoEm);
            Assert.Equal(doctorId, stored.CanceladoPorUserId);
            Assert.Equal("Registro duplicado", stored.MotivoCancelamento);
            Assert.Single(stored.Arquivos);
            Assert.Single(stored.Procedimentos);
        }
        Assert.Empty(storage.Deletes);

        Assert.DoesNotContain($"\"id\":{atendimentoId},", await doctor.GetStringAsync("/api/atendimentos-cirurgicos/"));
        Assert.DoesNotContain($"\"id\":{atendimentoId},", await doctor.GetStringAsync("/api/atendimentos-cirurgicos/?incluirCancelados=true"));
        Assert.DoesNotContain($"\"id\":{atendimentoId},", await admin.GetStringAsync("/api/atendimentos-cirurgicos/"));
        var adminWithCancelled = await admin.GetStringAsync("/api/atendimentos-cirurgicos/?incluirCancelados=true");
        Assert.Contains($"\"id\":{atendimentoId},", adminWithCancelled);
        Assert.Contains("Registro duplicado", adminWithCancelled);

        var update = await doctor.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{atendimentoId}", new
        {
            id = atendimentoId, dataProcedimento = new DateTime(2026, 10, 11), medicoResponsavelId = doctorId,
            status = AtendimentoCirurgicoStatus.Realizado, procedimentos = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        using var file = new MultipartFormDataContent();
        file.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("laudo")) { Headers = { ContentType = new("application/pdf") } }, "arquivo", "laudo.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.PostAsync($"/api/atendimentos-cirurgicos/{atendimentoId}/arquivos", file)).StatusCode);
        Assert.Empty(storage.Saves);
    }

    [Fact]
    public async Task AtendimentoFaturado_ContinuaSemPoderSerCancelado()
    {
        using var factory = CreateFactory(new RecordingStorage());
        var seed = await SeedFinanceiroAsync(factory);
        var doctorId = await UserIdAsync(factory, DoctorEmail);
        var atendimentoId = await SeedAtendimentoAsync(factory, seed.PacienteId, doctorId, faturado: true);
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/atendimentos-cirurgicos/{atendimentoId}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AtendimentosCirurgicos.SingleAsync(a => a.Id == atendimentoId);
        Assert.Null(stored.CanceladoEm);
        Assert.NotEqual(AtendimentoCirurgicoStatus.Cancelado, stored.Status);
    }

    [Fact]
    public async Task ClinicaB_NaoCancelaAtendimentoDaClinicaA()
    {
        using var factory = CreateFactory(new RecordingStorage());
        var seed = await SeedFinanceiroAsync(factory);
        var doctorId = await UserIdAsync(factory, DoctorEmail);
        var atendimentoId = await SeedAtendimentoAsync(factory, seed.PacienteId, doctorId, faturado: false);
        using var startup = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        using var betaAdmin = factory.CreateClient();
        await AuthenticateAsync(betaAdmin, beta.Slug);

        Assert.Equal(HttpStatusCode.NotFound, (await betaAdmin.DeleteAsync($"/api/atendimentos-cirurgicos/{atendimentoId}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AtendimentosCirurgicos.SingleAsync(a => a.Id == atendimentoId);
        Assert.Null(stored.CanceladoEm);
    }

    private static HemodinksApiFactory CreateFactory(RecordingStorage storage) => new(services =>
    {
        services.RemoveAll<IPatientFileStorage>();
        services.AddSingleton<IPatientFileStorage>(storage);
    });

    private static async Task<int> UserIdAsync(HemodinksApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .Where(u => u.ClinicaId == Clinica.DefaultId && u.Email == email).Select(u => u.Id).SingleAsync();
    }

    private static async Task<int> SeedAtendimentoAsync(HemodinksApiFactory factory, int patientId, int doctorId, bool faturado)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var atendimento = new AtendimentoCirurgico
        {
            ClinicaId = Clinica.DefaultId, PacienteId = patientId, MedicoResponsavelId = doctorId,
            DataProcedimento = new DateTime(2026, 10, 10), Status = AtendimentoCirurgicoStatus.Planejado
        };
        atendimento.Procedimentos.Add(new AtendimentoProcedimento
        {
            ClinicaId = Clinica.DefaultId, Descricao = "Procedimento preservado", Quantidade = 1m, PesoPercentual = 100m
        });
        atendimento.Arquivos.Add(new AtendimentoArquivo
        {
            ClinicaId = Clinica.DefaultId, NomeOriginal = "laudo.pdf", ContentType = "application/pdf",
            TamanhoBytes = 10, Url = "https://storage.test/patient-files/clinicas/1/laudo.pdf", DataUpload = DateTime.UtcNow
        });
        if (faturado)
            atendimento.Faturamentos.Add(new Faturamento { ClinicaId = Clinica.DefaultId, Competencia = new DateTime(2026, 10, 1) });
        db.AtendimentosCirurgicos.Add(atendimento);
        await db.SaveChangesAsync();
        return atendimento.Id;
    }

    private sealed class RecordingStorage : IPatientFileStorage
    {
        public List<string?> Deletes { get; } = [];
        public List<string> Saves { get; } = [];

        public Task<StoredPatientFile> SaveAsync(UploadedFile file, int clinicaId, CancellationToken cancellationToken)
        {
            Saves.Add(file.FileName);
            return Task.FromResult(new StoredPatientFile(file.FileName, file.ContentType, file.Length, "https://storage.test/novo.pdf"));
        }

        public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredPatientFileContent?>(new(new MemoryStream([1])));

        public Task DeleteAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken)
        {
            Deletes.Add(fileUrl);
            return Task.CompletedTask;
        }
    }
}
