using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Theory]
    [InlineData("data", "\"2026-02-30\"")]
    [InlineData("dataAtendimento", "\"2026-02-30\"")]
    [InlineData("data", "\"invalid-date\"")]
    [InlineData("dataAtendimento", "\"invalid-date\"")]
    [InlineData("data", "123")]
    [InlineData("dataAtendimento", "123")]
    public async Task PatientDates_MalformedCreateAndUpdate_Return400WithoutPersisting(string field, string dateToken)
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        using var beforeScope = factory.Services.CreateScope();
        beforeScope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(Clinica.DefaultId, Clinica.DefaultSlug);
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCount = await beforeDb.Users.CountAsync();
        var patientCount = await beforeDb.Pacientes.CountAsync();
        var billingCount = await beforeDb.FaturamentosMedicos.CountAsync();
        var payload = JsonSerializer.SerializeToNode(new
        {
            nomePaciente = "Malformed dates patient", medicoUserId = seed.MedicoId,
            data = "2026-10-22", dataAtendimento = "2026-10-23"
        })!.AsObject();
        payload[field] = JsonNode.Parse(dateToken);

        using var created = await client.PostAsJsonAsync("/api/pacientes/", payload);
        using var updated = await client.PutAsJsonAsync($"/api/pacientes/{seed.PacienteId}", payload);
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, updated.StatusCode);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(Clinica.DefaultId, Clinica.DefaultSlug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(userCount, await db.Users.CountAsync());
        Assert.Equal(patientCount, await db.Pacientes.CountAsync());
        Assert.Equal(billingCount, await db.FaturamentosMedicos.CountAsync());
        var patient = await db.Pacientes.Include(x => x.User).SingleAsync(x => x.Id == seed.PacienteId);
        Assert.Equal("Paciente Financeiro", patient.NomePaciente);
        Assert.Equal("Paciente Financeiro", patient.User.Nome);
        Assert.Null(patient.Data);
        Assert.Null(patient.DataAtendimento);
    }

    [Theory]
    [InlineData("2026-10-23", "2026-10-22")]
    [InlineData("2026-10-23T00:01:00+14:00", "2026-10-22T23:59:00-12:00")]
    public async Task PatientDates_InvalidCreateAndUpdate_Return400WithoutPersisting(string requestDate, string consolidationDate)
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        using var beforeScope = factory.Services.CreateScope();
        beforeScope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(Clinica.DefaultId, Clinica.DefaultSlug);
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCount = await beforeDb.Users.CountAsync();
        var patientCount = await beforeDb.Pacientes.CountAsync();
        var billingCount = await beforeDb.FaturamentosMedicos.CountAsync();
        var payload = new
        {
            nomePaciente = "Rejected patient", medicoUserId = seed.MedicoId,
            data = requestDate, dataAtendimento = consolidationDate,
            hospital = "Must not create hospital", convenio = "Must not create insurance"
        };

        using var created = await client.PostAsJsonAsync("/api/pacientes/", payload);
        using var updated = await client.PutAsJsonAsync($"/api/pacientes/{seed.PacienteId}", payload);
        foreach (var response in new[] { created, updated })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("A data da consolidação não pode ser anterior à data da solicitação.", await response.Content.ReadAsStringAsync());
        }

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(Clinica.DefaultId, Clinica.DefaultSlug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(userCount, await db.Users.CountAsync());
        Assert.Equal(patientCount, await db.Pacientes.CountAsync());
        Assert.Equal(billingCount, await db.FaturamentosMedicos.CountAsync());
        Assert.False(await db.Hospitais.AnyAsync(x => x.Nome == payload.hospital));
        Assert.False(await db.Convenios.AnyAsync(x => x.DescricaoConvenio == payload.convenio));
        var patient = await db.Pacientes.Include(x => x.User).SingleAsync(x => x.Id == seed.PacienteId);
        Assert.Equal("Paciente Financeiro", patient.NomePaciente);
        Assert.Equal("Paciente Financeiro", patient.User.Nome);
        Assert.Null(patient.Data);
        Assert.Null(patient.DataAtendimento);
    }

    [Theory]
    [InlineData("2026-10-22", "2026-10-23")]
    [InlineData("2026-10-22", "2026-10-22")]
    [InlineData("2026-10-22T23:59:00-12:00", "2026-10-22T00:01:00+14:00")]
    [InlineData("2026-10-22T23:59:00-12:00", "2026-10-23T00:01:00+14:00")]
    [InlineData("2026-10-22T23:59:00", "2026-10-22T00:01:00")]
    [InlineData(null, "2026-10-22")]
    [InlineData("2026-10-22", null)]
    [InlineData(null, null)]
    public async Task PatientDates_ValidCreateAndUpdate_PreserveCivilDates(string? requestDate, string? consolidationDate)
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var payload = new
        {
            nomePaciente = "Civil dates patient", medicoUserId = seed.MedicoId,
            data = requestDate, dataAtendimento = consolidationDate
        };

        using var created = await client.PostAsJsonAsync("/api/pacientes/", payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = await ReadJsonAsync(created);
        var id = json.RootElement.GetProperty("id").GetInt32();
        using var updated = await client.PutAsJsonAsync($"/api/pacientes/{seed.PacienteId}", payload);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(Clinica.DefaultId, Clinica.DefaultSlug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var patients = await db.Pacientes.Where(x => x.Id == id || x.Id == seed.PacienteId).ToListAsync();
        Assert.Equal(2, patients.Count);
        foreach (var patient in patients)
        {
            Assert.Equal(CivilDate(requestDate), patient.Data?.Date);
            Assert.Equal(CivilDate(consolidationDate), patient.DataAtendimento?.Date);
            Assert.Equal(Clinica.DefaultId, patient.ClinicaId);
        }
    }

    [Fact]
    public async Task PatientDates_ValidUpdateCannotMutatePatientInAnotherClinic()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        var beta = await SeedClinicaBetaAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        using var response = await client.PutAsJsonAsync($"/api/pacientes/{seed.PacienteId}", new
        {
            nomePaciente = "Foreign clinic attempt", data = "2026-10-22", dataAtendimento = "2026-10-23"
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var patient = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Pacientes
            .SingleAsync(x => x.Id == seed.PacienteId);
        Assert.Equal(Clinica.DefaultId, patient.ClinicaId);
        Assert.Equal("Paciente Financeiro", patient.NomePaciente);
        Assert.Null(patient.Data);
        Assert.Null(patient.DataAtendimento);
    }

    private static DateTime? CivilDate(string? value) => value is null ? null
        : DateTime.ParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
