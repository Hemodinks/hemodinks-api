extern alias workers;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using FileExportFunction = workers::HemodinksAPI.Workers.Functions.FileExportFunction;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task SurgeryExport_BillingUsesSameDoctorScopeAsQuery_WithoutBroadeningAssistantPermissions()
    {
        var queue = new CapturingFileExportQueue();
        using var factory = SurgeryExportFactory(queue);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, "maria.silva@email.com", TestPasswords.Valid);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doctor = await db.Users.SingleAsync(x => x.Email == "maria.silva@email.com");
            Assert.Equal(Perfil.MedicosId, doctor.PerfilId);
            var another = new User { Nome = "Other billing doctor", Email = "scope178@example.invalid",
                Telefone = "11999990000", Senha = "synthetic-hash", PerfilId = Perfil.MedicosId };
            var own = ExportPatient("SCOPE178-PRIMARY", 1, TipoCirurgia.Urgencia); own.MedicoUserId = doctor.Id;
            var assistant = ExportPatient("SCOPE178-ASSISTANT", 1, TipoCirurgia.Urgencia); assistant.MedicoAuxiliar1UserId = doctor.Id;
            var hidden = ExportPatient("SCOPE178-OTHER-PRIMARY", 1, TipoCirurgia.Urgencia);
            hidden.MedicoUser = another; hidden.MedicoAuxiliar1UserId = doctor.Id;
            db.Pacientes.AddRange(own, assistant, hidden);
            await db.SaveChangesAsync();
        }
        using var query = JsonDocument.Parse(await client.GetStringAsync("/api/faturamentos-medicos/?search=SCOPE178&tipoCirurgia=Urgencia"));
        Assert.Equal(2, query.RootElement.GetProperty("totalItems").GetInt32());
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/exports/", new
        {
            resource = "faturamentos-medicos", format = "xlsx", filters = new { search = "SCOPE178", tipoCirurgia = "Urgencia" }
        })).StatusCode);
        var snapshot = Assert.Single(queue.Messages).Snapshot!;
        Assert.Equal(2, snapshot.TotalRows);
        Assert.Equal(query.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("nomePaciente").GetString()),
            snapshot.Rows.Select(x => x[1]));
        Assert.DoesNotContain(snapshot.Rows, row => row.Any(value => value == "SCOPE178-OTHER-PRIMARY"));
    }

    [Theory]
    [InlineData("xlsx", "completo")]
    [InlineData("pdf", "completo")]
    [InlineData("xlsx", "sintetico")]
    [InlineData("pdf", "sintetico")]
    public async Task SurgeryExport_UsesCompleteScopedRowsAndRendersClinicalTypeAndTotals(string format, string reportType)
    {
        var queue = new CapturingFileExportQueue();
        using var factory = SurgeryExportFactory(queue);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var index = 0; index < 102; index++)
                db.Pacientes.Add(ExportPatient($"EXPORT178-{index:000}", 1, TipoCirurgia.Eletiva));
            db.Pacientes.Add(ExportPatient("EXPORT178-URGENTE", 1, TipoCirurgia.Urgencia));
            db.Clinicas.Add(new Clinica { Id = 17802, Nome = "Export B", Slug = "export-178-b" });
            db.Pacientes.Add(ExportPatient("EXPORT178-SEGREDO-B", 17802, TipoCirurgia.Eletiva));
            await db.SaveChangesAsync();
        }

        using var firstPage = JsonDocument.Parse(await client.GetStringAsync(
            "/api/pacientes/?search=EXPORT178&tipoCirurgia=Eletiva&sortBy=nome&sortDirection=asc&page=1&pageSize=100"));
        using var secondPage = JsonDocument.Parse(await client.GetStringAsync(
            "/api/pacientes/?search=EXPORT178&tipoCirurgia=Eletiva&sortBy=nome&sortDirection=asc&page=2&pageSize=100"));
        var expectedNames = firstPage.RootElement.GetProperty("items").EnumerateArray()
            .Concat(secondPage.RootElement.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("nomePaciente").GetString()).ToArray();
        Assert.Equal(102, expectedNames.Length);

        var response = await client.PostAsJsonAsync("/api/exports/", new
        {
            resource = "pacientes", format, reportType,
            currentUser = new { id = 1, perfilId = Perfil.SuperAdministradorId, clinicaId = 17802 },
            filters = new Dictionary<string, string?>
            {
                ["search"] = "EXPORT178", ["tipoCirurgia"] = "Eletiva", ["sortBy"] = "nome", ["sortDirection"] = "asc"
            }
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = Assert.Single(queue.Messages);
        Assert.Equal(1, message.ClinicaId);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.True(serialized.RootElement.TryGetProperty("snapshot", out var snapshot), "Export must capture authorized clinical rows, not only job metadata.");
        Assert.Equal(102, snapshot.GetProperty("totalRows").GetInt32());
        var totals = snapshot.GetProperty("totals").EnumerateArray().ToArray();
        Assert.Equal(102, totals.Sum(item => item.GetProperty("count").GetInt32()));
        Assert.Equal(1020m, totals.Sum(item => item.GetProperty("honorariosCirurgiao").GetDecimal()));

        var bytes = await CaptureSurgeryExportBytesAsync(message);
        string output;
        if (format == "xlsx")
        {
            using var archive = new ZipArchive(new MemoryStream(bytes));
            using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            var sheet = XDocument.Parse(await reader.ReadToEndAsync());
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            output = string.Join("\n", sheet.Descendants(ns + "t").Select(item => item.Value));
            Assert.All(sheet.Descendants(ns + "c"), cell => Assert.Equal("inlineStr", (string?)cell.Attribute("t")));
            if (reportType == "completo")
            {
                var names = sheet.Descendants(ns + "t").Select(item => item.Value)
                    .Where(value => value.StartsWith("EXPORT178-", StringComparison.Ordinal)).ToArray();
                Assert.Equal(expectedNames, names);
            }
        }
        else
        {
            output = Encoding.Latin1.GetString(bytes);
            Assert.StartsWith("%PDF-1.4", output);
            Assert.Contains("/Count ", output);
        }
        Assert.Contains("Eletiva", output);
        Assert.Contains("1020.00", output);
        Assert.DoesNotContain("SEGREDO-B", output);
        Assert.DoesNotContain("URGENTE", output);
        if (reportType == "completo") Assert.Contains("EXPORT178-101", output);
        else Assert.DoesNotContain("EXPORT178-000", output);
    }

    [Theory]
    [InlineData("clinicaId", "17802")]
    [InlineData("CurrentUserId", "1")]
    [InlineData("tipoCirurgia", "9")]
    [InlineData("sortDirection", "sideways")]
    [InlineData("dataInicio", "not-a-date")]
    [InlineData("page", "2")]
    public async Task SurgeryExport_RejectsUnsupportedOrInvalidFiltersWithoutQueueing(string key, string value)
    {
        var queue = new CapturingFileExportQueue();
        using var factory = SurgeryExportFactory(queue);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var response = await client.PostAsJsonAsync("/api/exports/", new
        {
            resource = "pacientes", format = "xlsx", filters = new Dictionary<string, string?> { [key] = value }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(queue.Messages);
    }

    [Fact]
    public async Task SurgeryExport_RejectsOversizedSnapshotWithoutTruncation()
    {
        var queue = new CapturingFileExportQueue();
        using var factory = SurgeryExportFactory(queue);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var index = 0; index < 80; index++)
            {
                var patient = ExportPatient($"EXPORT178-LARGE-{index}", 1, TipoCirurgia.Eletiva);
                patient.Procedimento = new string('x', 1000);
                db.Pacientes.Add(patient);
            }
            await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync("/api/exports/", new
        {
            resource = "pacientes", format = "xlsx", filters = new { search = "EXPORT178-LARGE" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("filtros", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(queue.Messages);
    }

    private static HemodinksApiFactory SurgeryExportFactory(CapturingFileExportQueue queue) => new(services =>
    {
        var descriptor = services.First(item => item.ServiceType == typeof(IFileExportQueue));
        services.Remove(descriptor);
        services.AddSingleton<IFileExportQueue>(queue);
    });

    private static Paciente ExportPatient(string name, int clinic, TipoCirurgia type) => new()
    {
        ClinicaId = clinic, NomePaciente = name, TipoCirurgia = type, Data = new DateTime(2026, 10, 19),
        User = new User
        {
            ClinicaId = clinic, Nome = name, Email = $"{Guid.NewGuid():N}@export.test", Senha = "fixture-only-hash",
            Telefone = "+5511999999911", PerfilId = Perfil.PacientesId, Ativo = true
        },
        FaturamentoMedico = new FaturamentoMedico { ClinicaId = clinic, HonorariosCirurgiao = 10m, RepasseMedico = 8m, ValorGlosa = 2m }
    };

    private static async Task<byte[]> CaptureSurgeryExportBytesAsync(FileExportQueueMessage message)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureWebJobsStorage"] = $"DefaultEndpointsProtocol=http;AccountName=exporttest;AccountKey={Convert.ToBase64String(new byte[32])};BlobEndpoint=http://127.0.0.1:{port}/exporttest;"
        }).Build();
        var server = Task.Run(async () =>
        {
            byte[] result = [];
            for (var index = 0; index < 2; index++)
            {
                var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                using var body = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(body, timeout.Token);
                Assert.Null(context.Request.Headers["x-ms-blob-public-access"]);
                if (index == 1)
                {
                    Assert.Contains($"clinica-{message.ClinicaId}/{message.Resource}/{message.JobId:N}.{message.Format}", context.Request.RawUrl);
                    result = body.ToArray();
                }
                context.Response.StatusCode = 201;
                context.Response.Headers["ETag"] = "\"export-etag\"";
                context.Response.Headers["Last-Modified"] = DateTime.UtcNow.ToString("R");
                context.Response.Close();
            }
            return result;
        }, timeout.Token);
        await new FileExportFunction(configuration, NullLogger<FileExportFunction>.Instance).GenerateFileExport(
            JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)), timeout.Token);
        return await server;
    }
}
