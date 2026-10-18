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
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, true, false)]
    public async Task SurgicalTeam_Patient_CreateAndEditEnforceRoleMatrix(bool principal, bool first, bool second, bool valid)
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        var payload = new
        {
            nomePaciente = "Synthetic surgery patient",
            medicoUserId = principal ? (int?)seed.MedicoId : null,
            medicoAuxiliar1UserId = first ? (int?)seed.MedicoId : null,
            medicoAuxiliar2UserId = second ? (int?)seed.MedicoId : null,
            procedimentos = new[] { new { cbhpmCodigo = seed.CbhpmCodigo } }
        };
        var created = await client.PostAsJsonAsync("/api/pacientes/", payload);
        Assert.Equal(valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest, created.StatusCode);
        if (!valid)
        {
            Assert.Contains(first && second ? "auxiliares" : "ao menos um medico", await created.Content.ReadAsStringAsync());
            return;
        }
        using var json = await ReadJsonAsync(created);
        var id = json.RootElement.GetProperty("id").GetInt32();
        var selected = json.RootElement.GetProperty("medicoUserId");
        Assert.Equal(payload.medicoUserId, selected.ValueKind == System.Text.Json.JsonValueKind.Null ? (int?)null : selected.GetInt32());
        var updated = await client.PutAsJsonAsync($"/api/pacientes/{id}", payload);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var invalid = await client.PutAsJsonAsync($"/api/pacientes/{id}", new { nomePaciente = payload.nomePaciente });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/pacientes/{id}", new
        {
            nomePaciente = payload.nomePaciente, medicoAuxiliar1UserId = seed.MedicoId,
            medicoAuxiliar2UserId = seed.MedicoId
        })).StatusCode);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(1, Clinica.DefaultSlug);
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Pacientes.SingleAsync(x => x.Id == id);
        Assert.Equal(payload.medicoUserId, stored.MedicoUserId);
        Assert.Equal(payload.medicoAuxiliar1UserId, stored.MedicoAuxiliar1UserId);
        Assert.Equal(payload.medicoAuxiliar2UserId, stored.MedicoAuxiliar2UserId);
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, true, false)]
    public async Task SurgicalTeam_Attendance_CreateAndEditEnforceRoleMatrix(bool principal, bool first, bool second, bool valid)
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        var payload = new
        {
            pacienteId = seed.PacienteId, dataProcedimento = new DateTime(2030, 1, 1),
            medicoResponsavelId = principal ? (int?)seed.MedicoId : null,
            medicoAuxiliar1Id = first ? (int?)seed.MedicoId : null,
            medicoAuxiliar2Id = second ? (int?)seed.MedicoId : null,
            procedimentos = new[] { new { cbhpmCodigo = seed.CbhpmCodigo, quantidade = 1m, pesoPercentual = 100m } }
        };
        var created = await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", payload);
        Assert.Equal(valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest, created.StatusCode);
        if (!valid) return;
        using var json = await ReadJsonAsync(created);
        var id = json.RootElement.GetProperty("id").GetInt32();
        var selected = json.RootElement.GetProperty("medicoResponsavelId");
        Assert.Equal(payload.medicoResponsavelId, selected.ValueKind == System.Text.Json.JsonValueKind.Null ? (int?)null : selected.GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{id}", payload)).StatusCode);
        var invalid = await client.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{id}", new
        {
            dataProcedimento = payload.dataProcedimento, procedimentos = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{id}", new
        {
            dataProcedimento = payload.dataProcedimento, medicoAuxiliar1Id = seed.MedicoId,
            medicoAuxiliar2Id = seed.MedicoId, procedimentos = payload.procedimentos
        })).StatusCode);
        using var billing = await client.PostAsJsonAsync("/api/faturamentos/", new
        {
            atendimentoCirurgicoId = id, competencia = new DateTime(2030, 1, 1)
        });
        Assert.Equal(HttpStatusCode.Created, billing.StatusCode);
        using var billingJson = await ReadJsonAsync(billing);
        Assert.Equal(1000m, billingJson.RootElement.GetProperty("valorApresentado").GetDecimal());
        using var summary = await client.GetAsync($"/api/financeiro/relatorios/resumo?medicoId={seed.MedicoId}");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        using var summaryJson = await ReadJsonAsync(summary);
        Assert.Equal(1000m, summaryJson.RootElement.GetProperty("valorApresentado").GetDecimal());
        using var list = await client.GetAsync($"/api/atendimentos-cirurgicos/?pacienteId={seed.PacienteId}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = await ReadJsonAsync(list);
        Assert.Single(listJson.RootElement.EnumerateArray());
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(1, Clinica.DefaultSlug);
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AtendimentosCirurgicos.SingleAsync(x => x.Id == id);
        Assert.Equal(payload.medicoResponsavelId, stored.MedicoResponsavelId);
        Assert.Equal(payload.medicoAuxiliar1Id, stored.MedicoAuxiliar1Id);
        Assert.Equal(payload.medicoAuxiliar2Id, stored.MedicoAuxiliar2Id);
    }

    [Fact]
    public async Task SurgicalTeam_DoctorCreatingAsAssistant_DoesNotBecomePrincipal()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        string email;
        int otherDoctorId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(1, Clinica.DefaultSlug);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doctor = await db.Users.SingleAsync(x => x.Id == seed.MedicoId);
            email = doctor.Email;
            doctor.Senha = new HemodinksAPI.Infrastructure.Utils.PasswordHasher().HashPassword(TestPasswords.Valid);
            var otherDoctor = new User
            {
                ClinicaId = 1, Nome = "Synthetic unrelated doctor", Email = "unrelated179@example.com",
                Telefone = string.Empty, Senha = "test-hash", PerfilId = Perfil.MedicosId, Ativo = true
            };
            db.Users.Add(otherDoctor);
            await db.SaveChangesAsync();
            otherDoctorId = otherDoctor.Id;
        }
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, email);
        using var patient = await client.PostAsJsonAsync("/api/pacientes/", new
        {
            nomePaciente = "Synthetic assistant patient", medicoAuxiliar1UserId = seed.MedicoId
        });
        Assert.Equal(HttpStatusCode.Created, patient.StatusCode);
        using var patientJson = await ReadJsonAsync(patient);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, patientJson.RootElement.GetProperty("medicoUserId").ValueKind);
        var payload = new
        {
            pacienteId = seed.PacienteId, dataProcedimento = new DateTime(2030, 1, 1), medicoAuxiliar1Id = seed.MedicoId,
            procedimentos = new[] { new { cbhpmCodigo = seed.CbhpmCodigo, quantidade = 1m, pesoPercentual = 100m } }
        };
        using var response = await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.RootElement.GetProperty("medicoResponsavelId").ValueKind);
        var unrelatedTeam = new
        {
            pacienteId = seed.PacienteId, dataProcedimento = payload.dataProcedimento,
            medicoAuxiliar1Id = otherDoctorId, procedimentos = payload.procedimentos
        };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", unrelatedTeam)).StatusCode);
        var attendanceId = json.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{attendanceId}", unrelatedTeam)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", new
        {
            pacienteId = seed.PacienteId, dataProcedimento = payload.dataProcedimento, procedimentos = payload.procedimentos
        })).StatusCode);
    }

    [Fact]
    public async Task SurgicalTeam_ClinicCannotReadEditOrAssignOtherClinicsDoctors()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        var beta = await SeedClinicaBetaAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);
        var patientPayload = new { nomePaciente = "Synthetic isolated patient", medicoAuxiliar1UserId = seed.MedicoId };
        using var patient = await client.PostAsJsonAsync("/api/pacientes/", patientPayload);
        Assert.Equal(HttpStatusCode.Created, patient.StatusCode);
        using var patientJson = await ReadJsonAsync(patient);
        var patientId = patientJson.RootElement.GetProperty("id").GetInt32();
        var attendancePayload = new
        {
            pacienteId = patientId, dataProcedimento = new DateTime(2030, 1, 1), medicoAuxiliar1Id = seed.MedicoId,
            procedimentos = new[] { new { cbhpmCodigo = seed.CbhpmCodigo, quantidade = 1m, pesoPercentual = 100m } }
        };
        using var attendance = await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", attendancePayload);
        Assert.Equal(HttpStatusCode.Created, attendance.StatusCode);
        using var attendanceJson = await ReadJsonAsync(attendance);
        var attendanceId = attendanceJson.RootElement.GetProperty("id").GetInt32();
        int foreignDoctorId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(beta.Id, beta.Slug);
            foreignDoctorId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
                .Where(x => x.PerfilId == Perfil.MedicosId).Select(x => x.Id).FirstAsync();
        }
        var foreignPatient = new { nomePaciente = "Synthetic isolated patient", medicoAuxiliar1UserId = foreignDoctorId };
        var foreignAttendance = new
        {
            pacienteId = patientId, dataProcedimento = attendancePayload.dataProcedimento,
            medicoAuxiliar1Id = foreignDoctorId, procedimentos = attendancePayload.procedimentos
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/pacientes/", foreignPatient)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/pacientes/{patientId}", foreignPatient)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/atendimentos-cirurgicos/", foreignAttendance)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{attendanceId}", foreignAttendance)).StatusCode);
        using var betaClient = factory.CreateClient();
        await AuthenticateAsync(betaClient, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        Assert.Equal(HttpStatusCode.NotFound, (await betaClient.GetAsync($"/api/pacientes/{patientId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await betaClient.PutAsJsonAsync($"/api/pacientes/{patientId}", patientPayload)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await betaClient.GetAsync($"/api/atendimentos-cirurgicos/{attendanceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await betaClient.PutAsJsonAsync($"/api/atendimentos-cirurgicos/{attendanceId}", attendancePayload)).StatusCode);
        using var billings = await betaClient.GetAsync("/api/faturamentos-medicos/?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, billings.StatusCode);
        using var billingsJson = await ReadJsonAsync(billings);
        Assert.DoesNotContain(billingsJson.RootElement.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetInt32() == patientId);
    }
}

