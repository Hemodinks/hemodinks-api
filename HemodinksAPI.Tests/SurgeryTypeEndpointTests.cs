using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SurgeryType_DefaultIsElective_AndOldUpdatePreservesUrgency(bool attendance)
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var path = attendance ? "/api/atendimentos-cirurgicos/" : "/api/pacientes/";
        object Payload(string name) => attendance ? new
        {
            pacienteId = seed.PacienteId, dataProcedimento = new DateTime(2030, 1, 1), medicoResponsavelId = seed.MedicoId,
            procedimentos = new[] { new { cbhpmCodigo = seed.CbhpmCodigo, quantidade = 1, pesoPercentual = 100 } }
        } : new { nomePaciente = name, medicoUserId = seed.MedicoId };
        var created = await client.PostAsJsonAsync(path, Payload("Elective record"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var elective = await ReadJsonAsync(created);
        Assert.Equal("Eletiva", elective.RootElement.GetProperty("tipoCirurgia").GetString());
        var id = elective.RootElement.GetProperty("id").GetInt32();
        var json = System.Text.Json.JsonSerializer.SerializeToNode(Payload("Urgent record"))!.AsObject();
        json["tipoCirurgia"] = "Urgencia";
        var urgentCreation = await client.PostAsJsonAsync(path, json);
        Assert.Equal(HttpStatusCode.Created, urgentCreation.StatusCode);
        using var createdUrgent = await ReadJsonAsync(urgentCreation);
        Assert.Equal("Urgencia", createdUrgent.RootElement.GetProperty("tipoCirurgia").GetString());
        var updated = await client.PutAsJsonAsync($"{path}{id}", json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var urgent = await ReadJsonAsync(updated);
        Assert.Equal("Urgencia", urgent.RootElement.GetProperty("tipoCirurgia").GetString());
        var oldClient = await client.PutAsJsonAsync($"{path}{id}", Payload("Old client update"));
        Assert.Equal(HttpStatusCode.OK, oldClient.StatusCode);
        using var preserved = await ReadJsonAsync(oldClient);
        Assert.Equal("Urgencia", preserved.RootElement.GetProperty("tipoCirurgia").GetString());
        json["tipoCirurgia"] = 999;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{path}{id}", json)).StatusCode);
        using var detail = await ReadJsonAsync(await client.GetAsync($"{path}{id}"));
        Assert.Equal("Urgencia", detail.RootElement.GetProperty("tipoCirurgia").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SurgeryType_InvalidValuesAreRejected(bool attendance)
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var path = attendance ? "/api/atendimentos-cirurgicos/" : "/api/pacientes/";
        var payload = new
        {
            nomePaciente = "Invalid type", medicoUserId = seed.MedicoId, pacienteId = seed.PacienteId,
            dataProcedimento = new DateTime(2030, 1, 1), medicoResponsavelId = seed.MedicoId,
            tipoCirurgia = 999, procedimentos = new[] { new { cbhpmCodigo = seed.CbhpmCodigo, quantidade = 1, pesoPercentual = 100 } }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "?tipoCirurgia=999")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SurgeryType_ClinicBResourceCannotBeReadEditedOrInferredByClinicA(bool attendance)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        int id;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinica { Nome = "Other type clinic", Slug = "other-type-clinic" };
            db.Clinicas.Add(clinic); await db.SaveChangesAsync();
            var doctor = new User { ClinicaId = clinic.Id, Nome = "Other doctor", Email = "other-type@example.invalid",
                Telefone = "11999990000", Senha = "synthetic-hash", PerfilId = Perfil.MedicosId };
            var patient = new Paciente { ClinicaId = clinic.Id, NomePaciente = "Other surgery", TipoCirurgia = TipoCirurgia.Urgencia,
                MedicoUser = doctor, User = new User { ClinicaId = clinic.Id, Nome = "Other patient", Email = "other-patient-type@example.invalid",
                    Telefone = "11999990000", Senha = "synthetic-hash", PerfilId = Perfil.PacientesId } };
            var surgery = new AtendimentoCirurgico { ClinicaId = clinic.Id, Paciente = patient,
                MedicoResponsavel = doctor, DataProcedimento = new(2030, 1, 1), TipoCirurgia = TipoCirurgia.Urgencia };
            db.AtendimentosCirurgicos.Add(surgery); await db.SaveChangesAsync();
            id = attendance ? surgery.Id : patient.Id;
        }
        var path = attendance ? "/api/atendimentos-cirurgicos/" : "/api/pacientes/";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path + id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(path + id,
            new { nomePaciente = "Attempt", tipoCirurgia = "Eletiva", procedimentos = Array.Empty<object>() })).StatusCode);
        using var list = await ReadJsonAsync(await client.GetAsync(path + "?tipoCirurgia=Urgencia&clinicaId=2"));
        if (attendance) Assert.Equal(0, list.RootElement.GetArrayLength());
        else Assert.Equal(0, list.RootElement.GetProperty("totalItems").GetInt32());
        using var verify = factory.Services.CreateScope();
        verify.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(TipoCirurgia.Urgencia, attendance
            ? (await verifyDb.AtendimentosCirurgicos.SingleAsync(x => x.Id == id)).TipoCirurgia
            : (await verifyDb.Pacientes.SingleAsync(x => x.Id == id)).TipoCirurgia);
    }
}
