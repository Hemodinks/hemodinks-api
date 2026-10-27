using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed class PacienteFinanceiroAuthorizationEndpointTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TeamSummary_RequiresActiveMemberOnPatient_AndPreservesClinicIsolation(int doctorSlot)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var team = fixture.Anonymous;
        int allowedId, deniedId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var allowed = Patient(team.ClinicId, "allowed");
            if (doctorSlot == 0) allowed.MedicoUserId = team.MemberUserId;
            if (doctorSlot == 1) allowed.MedicoAuxiliar1UserId = team.MemberUserId;
            if (doctorSlot == 2) allowed.MedicoAuxiliar2UserId = team.MemberUserId;
            var denied = Patient(team.ClinicId, "denied");
            denied.MedicoUserId = fixture.Selection.MemberUserId;
            db.Pacientes.AddRange(allowed, denied);
            await db.SaveChangesAsync();
            allowedId = allowed.Id;
            deniedId = denied.Id;
        }
        var login = await TeamLoginSecurityTests.LoginAsync(client, team);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var allowedResponse = await client.GetAsync($"/api/pacientes/{allowedId}/resumo-financeiro");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        var summary = await allowedResponse.Content.ReadFromJsonAsync<Application.Features.Financeiro.PacienteFinanceiroResumoDto>();
        Assert.NotNull(summary);
        Assert.Equal(150m, summary.ValorApresentado);
        Assert.Equal(130m, summary.ValorRecebido);
        Assert.Equal("Legado", summary.OrigemDados);

        // Client-supplied team identifiers must not override the authenticated team.
        var deniedResponse = await client.GetAsync($"/api/pacientes/{deniedId}/resumo-financeiro?equipeId={fixture.Selection.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        Assert.DoesNotContain("valorApresentado", await deniedResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/pacientes/{fixture.OtherPatientId}/resumo-financeiro")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var member = await db.EquipeMembros.SingleAsync(x => x.EquipeId == team.Id && x.UserId == team.MemberUserId);
            member.Ativo = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/pacientes/{allowedId}/resumo-financeiro")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/pacientes/{allowedId}/resumo-financeiro")).StatusCode);
    }

    private static Paciente Patient(int clinicId, string key) => new()
    {
        ClinicaId = clinicId, NomePaciente = $"Synthetic {key}", Pagamento = "150.00",
        RepasseGlosa = "20.00", StatusPago = true, DataAtendimento = new DateTime(2030, 1, 1),
        User = new User
        {
            ClinicaId = clinicId, Nome = $"Synthetic {key}", Email = $"{key}-{Guid.NewGuid():N}@example.com",
            Telefone = "+5511999999999", Senha = "test-hash", PerfilId = Perfil.PacientesId
        }
    };
}
