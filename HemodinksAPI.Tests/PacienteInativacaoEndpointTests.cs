using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

// #369: DELETE de paciente vira inativacao; prontuario preservado, some das listas, so Admin ve e reativa.
public sealed class PacienteInativacaoEndpointTests : ApiEndpointTestBase
{
    private const string PatientName = "Paciente Inativacao Beta";

    [Fact]
    public async Task Inativar_PreservaDados_EscondeDoMedico_AdminListaEReativa()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        var (patientId, doctorId) = await SeedBetaPatientAsync(factory, beta.Id);
        var attendanceId = await SeedAttendanceAsync(factory, beta.Id, patientId, doctorId);

        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, beta.Slug);
        using var doctor = factory.CreateClient();
        await AuthenticateAsync(doctor, beta.Slug, "dra.beta@hemodinks.com");

        Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync($"/api/pacientes/{patientId}")).StatusCode);
        await AssertDoctorSeesClinicalRecordsAsync(doctor, patientId, attendanceId, visible: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.DeleteAsync($"/api/pacientes/{patientId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/pacientes/{patientId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/pacientes/{patientId}")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Pacientes.Include(p => p.User).SingleAsync(p => p.Id == patientId);
            Assert.False(stored.Ativo);
            Assert.NotNull(stored.InativadoEm);
            Assert.NotNull(stored.InativadoPorUserId);
            Assert.True(await db.Observacoes.AnyAsync(o => o.PacienteId == patientId));
        }

        // Medico: some de leitura, listas (mesmo pedindo "todos"), edicao e reativacao.
        Assert.Equal(HttpStatusCode.NotFound, (await doctor.GetAsync($"/api/pacientes/{patientId}")).StatusCode);
        Assert.DoesNotContain(PatientName, await doctor.GetStringAsync("/api/pacientes/?page=1&pageSize=50"));
        Assert.DoesNotContain(PatientName, await doctor.GetStringAsync("/api/pacientes/?page=1&pageSize=50&situacao=todos"));
        var update = await doctor.PutAsJsonAsync($"/api/pacientes/{patientId}", new { nomePaciente = "Alterado", medicoUserId = doctorId });
        Assert.False(update.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.PostAsync($"/api/pacientes/{patientId}/reativacao", null)).StatusCode);
        await AssertDoctorSeesClinicalRecordsAsync(doctor, patientId, attendanceId, visible: false);
        using (var attendanceFile = new MultipartFormDataContent())
        {
            attendanceFile.Add(new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("application/pdf") } }, "arquivo", "laudo.pdf");
            Assert.Equal(HttpStatusCode.NotFound,
                (await doctor.PostAsync($"/api/atendimentos-cirurgicos/{attendanceId}/arquivos", attendanceFile)).StatusCode);
        }
        var newAttendance = await doctor.PostAsJsonAsync("/api/atendimentos-cirurgicos/", new
        {
            pacienteId = patientId, dataProcedimento = new DateTime(2026, 7, 10), medicoResponsavelId = doctorId,
            procedimentos = new[] { new { descricao = "Procedimento", quantidade = 1m, pesoPercentual = 100m } }
        });
        Assert.Equal(HttpStatusCode.NotFound, newAttendance.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/atendimentos-cirurgicos/{attendanceId}")).StatusCode);

        // Administrador: lista padrao sem o inativo; filtro de inativos e leitura direta mostram.
        Assert.DoesNotContain(PatientName, await admin.GetStringAsync("/api/pacientes/?page=1&pageSize=50"));
        using (var inactiveList = await ReadJsonAsync(await admin.GetAsync("/api/pacientes/?page=1&pageSize=50&situacao=inativos")))
        {
            var item = Assert.Single(inactiveList.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(patientId, item.GetProperty("id").GetInt32());
            Assert.True(item.GetProperty("inativado").GetBoolean());
        }
        using (var detail = await ReadJsonAsync(await admin.GetAsync($"/api/pacientes/{patientId}")))
            Assert.True(detail.RootElement.GetProperty("inativado").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/pacientes/?situacao=apagados")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/pacientes/{patientId}/reativacao", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync($"/api/pacientes/{patientId}")).StatusCode);
        Assert.Contains(PatientName, await doctor.GetStringAsync("/api/pacientes/?page=1&pageSize=50"));
        await AssertDoctorSeesClinicalRecordsAsync(doctor, patientId, attendanceId, visible: true);
    }

    [Fact]
    public async Task InativarEReativar_PacienteDeOutraClinica_404_SemRevelarNemAlterar()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        var (patientId, _) = await SeedBetaPatientAsync(factory, beta.Id);

        using var defaultAdmin = factory.CreateClient();
        await AuthenticateAsync(defaultAdmin, Clinica.DefaultSlug);
        var delete = await defaultAdmin.DeleteAsync($"/api/pacientes/{patientId}");
        var reactivate = await defaultAdmin.PostAsync($"/api/pacientes/{patientId}/reativacao", null);
        var missing = await defaultAdmin.DeleteAsync("/api/pacientes/987654");

        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reactivate.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await delete.Content.ReadAsStringAsync());
        Assert.DoesNotContain(PatientName, await reactivate.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Pacientes.SingleAsync(p => p.Id == patientId);
        Assert.True(stored.Ativo);
        Assert.Null(stored.InativadoEm);
    }

    // Faturamento medico, resumo financeiro e atendimentos tambem escondem o paciente inativado (#369).
    private static async Task AssertDoctorSeesClinicalRecordsAsync(HttpClient doctor, int patientId, int attendanceId, bool visible)
    {
        var expected = visible ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        var billing = await doctor.GetStringAsync("/api/faturamentos-medicos/?page=1&pageSize=50");
        var attendances = await doctor.GetStringAsync("/api/atendimentos-cirurgicos/");
        if (visible)
        {
            Assert.Contains(PatientName, billing);
            Assert.Contains(PatientName, attendances);
        }
        else
        {
            Assert.DoesNotContain(PatientName, billing);
            Assert.DoesNotContain(PatientName, attendances);
        }
        Assert.Equal(expected, (await doctor.GetAsync($"/api/atendimentos-cirurgicos/{attendanceId}")).StatusCode);
        Assert.Equal(expected, (await doctor.GetAsync($"/api/pacientes/{patientId}/resumo-financeiro")).StatusCode);
    }

    private static async Task<int> SeedAttendanceAsync(HemodinksApiFactory factory, int clinicId, int patientId, int doctorId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attendance = new AtendimentoCirurgico
        {
            ClinicaId = clinicId, PacienteId = patientId, MedicoResponsavelId = doctorId, DataProcedimento = new DateTime(2026, 7, 1)
        };
        db.AtendimentosCirurgicos.Add(attendance);
        await db.SaveChangesAsync();
        return attendance.Id;
    }

    private static async Task<(int PatientId, int DoctorId)> SeedBetaPatientAsync(HemodinksApiFactory factory, int clinicId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctorId = await db.Users.Where(u => u.ClinicaId == clinicId && u.Email == "dra.beta@hemodinks.com")
            .Select(u => u.Id).SingleAsync();
        var user = new User
        {
            ClinicaId = clinicId, Nome = PatientName, Email = $"inativacao-{Guid.NewGuid():N}@teste.local",
            Telefone = "+5511977770000", Senha = "hash", PerfilId = Perfil.PacientesId, Ativo = true
        };
        var patient = new Paciente { ClinicaId = clinicId, User = user, NomePaciente = PatientName, MedicoUserId = doctorId };
        patient.Observacoes.Add(new Observacao
        {
            ClinicaId = clinicId, Texto = "Registro clinico preservado", AutorUserId = doctorId, DestinatarioUserId = doctorId
        });
        db.Pacientes.Add(patient);
        await db.SaveChangesAsync();
        return (patient.Id, doctorId);
    }
}
