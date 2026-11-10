using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed class DataConsolidacaoPendenciaEndpointTests : ApiEndpointTestBase
{
    private const string PendenciasUrl = "/api/pacientes/pendencias/data-consolidacao";
    private static int AnoMaximo => DataConsolidacaoRules.AnoMaximo(DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(2025)]
    [InlineData(2016)]
    [InlineData(8202)]
    public async Task Create_WithConsolidationYearBelowMinimumOrFarFuture_Returns400(int year)
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        using var response = await client.PostAsJsonAsync("/api/pacientes/", new
        {
            nomePaciente = "Ano invalido", medicoUserId = seed.MedicoId, dataAtendimento = $"{year:D4}-05-10"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"entre {DataConsolidacaoRules.AnoMinimo} e {AnoMaximo}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_WithConsolidationYearAboveMaximum_Returns400()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        using var response = await client.PostAsJsonAsync("/api/pacientes/", new
        {
            nomePaciente = "Ano futuro", medicoUserId = seed.MedicoId, dataAtendimento = $"{AnoMaximo + 1}-01-01"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("2026-01-01", "2026-01-01")]
    [InlineData("2026-03-01", null)]
    [InlineData(null, null)]
    public async Task Create_WithinRangeAndRequestNotAfterConsolidation_Returns201(string? consolidation, string? request)
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        using var response = await client.PostAsJsonAsync("/api/pacientes/", new
        {
            nomePaciente = "Ano valido", medicoUserId = seed.MedicoId, data = request, dataAtendimento = consolidation
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_AtMaximumYear_Returns201()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        using var response = await client.PostAsJsonAsync("/api/pacientes/", new
        {
            nomePaciente = "Ano maximo", medicoUserId = seed.MedicoId, dataAtendimento = $"{AnoMaximo}-12-31"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Update_LegacyOutOfRangeDateUnchanged_IsAllowed_ButChangingToOutOfRangeIsRejected()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        var legacyId = await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2016, 4, 2), seed.MedicoId);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        using var keepLegacy = await client.PutAsJsonAsync($"/api/pacientes/{legacyId}", new
        {
            nomePaciente = "Legado renomeado", medicoUserId = seed.MedicoId, dataAtendimento = "2016-04-02"
        });
        Assert.Equal(HttpStatusCode.OK, keepLegacy.StatusCode);

        using var otherInvalid = await client.PutAsJsonAsync($"/api/pacientes/{legacyId}", new
        {
            nomePaciente = "Legado renomeado", medicoUserId = seed.MedicoId, dataAtendimento = "2045-04-02"
        });
        Assert.Equal(HttpStatusCode.BadRequest, otherInvalid.StatusCode);
        Assert.Contains("O ano da data da consolidação deve estar entre", await otherInvalid.Content.ReadAsStringAsync());

        using var fixedDate = await client.PutAsJsonAsync($"/api/pacientes/{legacyId}", new
        {
            nomePaciente = "Legado corrigido", medicoUserId = seed.MedicoId, dataAtendimento = "2026-04-02"
        });
        Assert.Equal(HttpStatusCode.OK, fixedDate.StatusCode);

        var stored = await ReadPacienteAsync(factory, legacyId);
        Assert.Equal(new DateTime(2026, 4, 2), stored.DataAtendimento?.Date);
    }

    [Fact]
    public async Task Update_LegacyPatientOfAnotherClinic_Returns404WithoutRevealingDateRule()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        var beta = await SeedClinicaBetaAsync(factory);
        var legacyId = await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2016, 4, 2), seed.MedicoId);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        using var response = await client.PutAsJsonAsync($"/api/pacientes/{legacyId}", new
        {
            nomePaciente = "Tentativa entre clinicas", dataAtendimento = "2045-04-02"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("data da consolidação", await response.Content.ReadAsStringAsync());
        var stored = await ReadPacienteAsync(factory, legacyId);
        Assert.Equal(new DateTime(2016, 4, 2), stored.DataAtendimento?.Date);
    }

    [Fact]
    public async Task Pendencias_Admin_CountsAndFiltersOnlyOutOfRangePatientsOfOwnClinic()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        var beta = await SeedClinicaBetaAsync(factory);
        var expected = new[]
        {
            await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2016, 1, 5), seed.MedicoId),
            await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2045, 7, 1), seed.MedicoId),
            await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(8202, 5, 20), seed.MedicoId),
            await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2025, 12, 31), seed.MedicoId)
        };
        await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2026, 1, 1), seed.MedicoId);
        await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(AnoMaximo, 12, 31), seed.MedicoId);
        await SeedPacienteAsync(factory, Clinica.DefaultId, null, seed.MedicoId);
        var foreignId = await SeedPacienteAsync(factory, beta.Id, new DateTime(2016, 1, 5), null);

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);

        using var pendencias = await ReadJsonAsync(await client.GetAsync(PendenciasUrl));
        Assert.Equal(4, pendencias.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(DataConsolidacaoRules.AnoMinimo, pendencias.RootElement.GetProperty("anoMinimo").GetInt32());
        Assert.Equal(AnoMaximo, pendencias.RootElement.GetProperty("anoMaximo").GetInt32());

        var ids = await ListPendingIdsAsync(client);
        Assert.Equal(expected.Order(), ids.Order());
        Assert.DoesNotContain(foreignId, ids);
    }

    [Fact]
    public async Task Pendencias_OtherClinic_DoesNotSeeOrCountClinicAPatients()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        var beta = await SeedClinicaBetaAsync(factory);
        await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2016, 1, 5), seed.MedicoId);
        await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(8202, 1, 5), seed.MedicoId);

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        using var pendencias = await ReadJsonAsync(await client.GetAsync(PendenciasUrl));
        Assert.Equal(0, pendencias.RootElement.GetProperty("total").GetInt32());
        Assert.Empty(await ListPendingIdsAsync(client));
    }

    [Fact]
    public async Task Pendencias_Medico_SeesOnlyWhereHeIsSurgeonOrAuxiliary_NotOtherGroupPatients()
    {
        using var factory = new HemodinksApiFactory();
        var medicoA = await SeedLoginUserAsync(factory, Perfil.MedicosId, "medico-a");
        var medicoB = await SeedLoginUserAsync(factory, Perfil.MedicosId, "medico-b");
        await SeedGrupoMedicoAsync(factory, medicoA.Id, medicoB.Id);
        var asSurgeon = await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2016, 1, 5), medicoA.Id);
        var asAux1 = await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2045, 1, 5), medicoB.Id, aux1: medicoA.Id);
        var asAux2 = await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(8202, 1, 5), medicoB.Id, aux2: medicoA.Id);
        var groupOnly = await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2016, 3, 5), medicoB.Id);
        await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2026, 6, 5), medicoA.Id);

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, medicoA.Email, TestPasswords.Valid);

        // O grupo médico dá acesso ao paciente do colega na listagem normal...
        using var allList = await ReadJsonAsync(await client.GetAsync("/api/pacientes/?pageSize=100"));
        Assert.Contains(groupOnly, allList.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()));

        // ...mas o aviso e o filtro de pendências ficam só com cirurgião, auxiliar 1 e auxiliar 2.
        using var pendencias = await ReadJsonAsync(await client.GetAsync(PendenciasUrl));
        Assert.Equal(3, pendencias.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(new[] { asSurgeon, asAux1, asAux2 }.Order(), (await ListPendingIdsAsync(client)).Order());
    }

    [Fact]
    public async Task Pendencias_Controller_IsAllowed()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        var controller = await SeedLoginUserAsync(factory, Perfil.ControllerId, "controller");
        await SeedPacienteAsync(factory, Clinica.DefaultId, new DateTime(2016, 1, 5), seed.MedicoId);

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, controller.Email, TestPasswords.Valid);

        using var response = await client.GetAsync(PendenciasUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Pendencias_PatientProfile_IsForbidden()
    {
        using var factory = new HemodinksApiFactory();
        var paciente = await SeedLoginUserAsync(factory, Perfil.PacientesId, "paciente");

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, paciente.Email, TestPasswords.Valid);

        using var response = await client.GetAsync(PendenciasUrl);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pendencias_Anonymous_IsUnauthorized()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(PendenciasUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<int[]> ListPendingIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/pacientes/?pendenciaDataConsolidacao=true&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).ToArray();
    }

    private static async Task<int> SeedPacienteAsync(HemodinksApiFactory factory, int clinicaId, DateTime? dataConsolidacao, int? medicoUserId,
        int? aux1 = null, int? aux2 = null)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User
        {
            ClinicaId = clinicaId, Nome = $"Paciente {suffix[..8]}", Telefone = "+5511977777777",
            Email = $"pend-{suffix}@teste.local", Senha = "hash", PerfilId = Perfil.PacientesId, Ativo = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var paciente = new Paciente
        {
            ClinicaId = clinicaId, UserId = user.Id, NomePaciente = user.Nome,
            DataAtendimento = dataConsolidacao, MedicoUserId = medicoUserId,
            MedicoAuxiliar1UserId = aux1, MedicoAuxiliar2UserId = aux2
        };
        db.Pacientes.Add(paciente);
        await db.SaveChangesAsync();
        return paciente.Id;
    }

    private static async Task SeedGrupoMedicoAsync(HemodinksApiFactory factory, params int[] userIds)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grupo = new GrupoMedico { ClinicaId = Clinica.DefaultId, Nome = $"Grupo {Guid.NewGuid():N}"[..20] };
        foreach (var userId in userIds)
            grupo.Membros.Add(new GrupoMedicoUsuario { ClinicaId = Clinica.DefaultId, UserId = userId });
        db.GruposMedicos.Add(grupo);
        await db.SaveChangesAsync();
    }

    private static async Task<User> SeedLoginUserAsync(HemodinksApiFactory factory, int perfilId, string prefix)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User
        {
            ClinicaId = Clinica.DefaultId, Nome = $"{prefix} {suffix}", Telefone = "+5511966666666",
            Email = $"{prefix}-{suffix}@teste.local", Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
            PerfilId = perfilId, Ativo = true, PrecisaTrocarSenha = false,
            Crm = perfilId == Perfil.MedicosId ? suffix[..6] : null, CrmUf = perfilId == Perfil.MedicosId ? "SP" : null
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Paciente> ReadPacienteAsync(HemodinksApiFactory factory, int id)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(Clinica.DefaultId, Clinica.DefaultSlug);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Pacientes.AsNoTracking().SingleAsync(x => x.Id == id);
    }
}

public sealed class DataConsolidacaoRulesTests
{
    [Theory]
    // 31/12/2026 23h em Brasília já é 2027 em UTC: o limite segue o ano local.
    [InlineData("2027-01-01T02:00:00Z", 2027)]
    [InlineData("2027-01-01T03:00:00Z", 2028)]
    [InlineData("2026-06-15T12:00:00Z", 2027)]
    public void AnoMaximo_UsesBrasiliaCivilYearPlusOne(string utcNow, int expected)
    {
        Assert.Equal(expected, DataConsolidacaoRules.AnoMaximo(DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(2025, false)]
    [InlineData(2026, true)]
    [InlineData(2027, true)]
    [InlineData(2028, false)]
    [InlineData(8202, false)]
    public void IsConforme_RespectsMinimumAndMaximum(int year, bool expected)
    {
        Assert.Equal(expected, DataConsolidacaoRules.IsConforme(new DateTime(year, 6, 1), 2027));
    }
}
