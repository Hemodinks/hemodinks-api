using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Features.Faturamentos.Queries;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

// Issue #245: status da cirurgia derivado da data da consolidação (DataAtendimento).
public sealed class SurgeryStatusTests : ApiEndpointTestBase
{
    [Fact]
    public void Status_FollowsConsolidationDate()
    {
        var paciente = new Paciente { NomePaciente = "Synthetic" };
        Assert.Equal(StatusCirurgia.NaoConsolidada, paciente.StatusCirurgia);

        paciente.DataAtendimento = new DateTime(2026, 10, 1);
        Assert.Equal(StatusCirurgia.Consolidada, paciente.StatusCirurgia);

        paciente.DataAtendimento = null;
        Assert.Equal(StatusCirurgia.NaoConsolidada, paciente.StatusCirurgia);
    }

    [Fact]
    public async Task StatusFilters_SelectOnlyMatchingRecords_WithinCurrentClinic()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var seed = new PlatformDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Clinicas.Add(new Clinica { Id = 2, Nome = "Other", Slug = "other-status" });
            seed.Pacientes.AddRange(
                Patient(1, "consolidated", new DateTime(2026, 10, 1)),
                Patient(1, "pending", null),
                Patient(2, "remote-consolidated", new DateTime(2026, 10, 1)));
            await seed.SaveChangesAsync();
        }

        var tenant = new ClinicaContext();
        tenant.SetCurrent(1, Clinica.DefaultSlug);
        await using var db = new AppDbContext(options, tenant);
        var patients = new GetAllPacientesQueryHandler(db, NullLogger<GetAllPacientesQueryHandler>.Instance);
        var billing = new GetAllFaturamentosMedicosQueryHandler(db, NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance);

        var consolidated = await patients.Handle(new() { CurrentPerfilId = Perfil.SuperAdministradorId,
            StatusCirurgia = StatusCirurgia.Consolidada }, default);
        var item = Assert.Single(consolidated.Items);
        Assert.Equal("consolidated", item.NomePaciente);
        Assert.Equal(StatusCirurgia.Consolidada, item.StatusCirurgia);

        var pending = await patients.Handle(new() { CurrentPerfilId = Perfil.AdministradorId,
            StatusCirurgia = StatusCirurgia.NaoConsolidada }, default);
        Assert.Equal("pending", Assert.Single(pending.Items).NomePaciente);

        var all = await patients.Handle(new() { CurrentPerfilId = Perfil.AdministradorId }, default);
        Assert.Equal(2, all.TotalItems);
        Assert.DoesNotContain(all.Items, x => x.NomePaciente == "remote-consolidated");

        var billed = await billing.Handle(new() { CurrentPerfilId = Perfil.AdministradorId,
            StatusCirurgia = StatusCirurgia.Consolidada }, default);
        Assert.Equal("consolidated", Assert.Single(billed.Items).NomePaciente);
        Assert.Equal(1, billed.TotalItems);
        var billedPending = await billing.Handle(new() { CurrentPerfilId = Perfil.AdministradorId,
            StatusCirurgia = StatusCirurgia.NaoConsolidada }, default);
        Assert.Equal("pending", Assert.Single(billedPending.Items).NomePaciente);

        await Assert.ThrowsAsync<InvalidOperationException>(() => patients.Handle(new() { StatusCirurgia = (StatusCirurgia)999 }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => billing.Handle(new() { StatusCirurgia = (StatusCirurgia)999 }, default));
    }

    [Fact]
    public async Task Status_IsDerivedOnCreateAndUpdate_AndFiltersTheList()
    {
        using var factory = new HemodinksApiFactory();
        var seed = await SeedFinanceiroAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var created = await client.PostAsJsonAsync("/api/pacientes/", new { nomePaciente = "Status pending", medicoUserId = seed.MedicoId });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var pending = await ReadJsonAsync(created);
        Assert.Equal("NaoConsolidada", pending.RootElement.GetProperty("statusCirurgia").GetString());
        var id = pending.RootElement.GetProperty("id").GetInt32();

        var consolidatedCreation = await client.PostAsJsonAsync("/api/pacientes/", new
        {
            nomePaciente = "Status consolidated", medicoUserId = seed.MedicoId, dataAtendimento = new DateTime(2026, 10, 1)
        });
        Assert.Equal(HttpStatusCode.Created, consolidatedCreation.StatusCode);
        using var consolidatedCreated = await ReadJsonAsync(consolidatedCreation);
        Assert.Equal("Consolidada", consolidatedCreated.RootElement.GetProperty("statusCirurgia").GetString());

        // O cliente não define o status: um valor enviado no payload é ignorado.
        var updated = await client.PutAsJsonAsync($"/api/pacientes/{id}", new
        {
            nomePaciente = "Status pending", medicoUserId = seed.MedicoId,
            dataAtendimento = new DateTime(2026, 10, 2), statusCirurgia = "NaoConsolidada"
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var consolidated = await ReadJsonAsync(updated);
        Assert.Equal("Consolidada", consolidated.RootElement.GetProperty("statusCirurgia").GetString());

        var cleared = await client.PutAsJsonAsync($"/api/pacientes/{id}", new { nomePaciente = "Status pending", medicoUserId = seed.MedicoId });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var reverted = await ReadJsonAsync(cleared);
        Assert.Equal("NaoConsolidada", reverted.RootElement.GetProperty("statusCirurgia").GetString());

        using var onlyConsolidated = await ReadJsonAsync(await client.GetAsync("/api/pacientes/?statusCirurgia=Consolidada&pageSize=100"));
        var consolidatedItems = onlyConsolidated.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(consolidatedItems, x => x.GetProperty("nomePaciente").GetString() == "Status consolidated");
        Assert.All(consolidatedItems, x => Assert.Equal("Consolidada", x.GetProperty("statusCirurgia").GetString()));

        using var onlyPending = await ReadJsonAsync(await client.GetAsync("/api/pacientes/?statusCirurgia=NaoConsolidada&pageSize=100"));
        var pendingItems = onlyPending.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(pendingItems, x => x.GetProperty("id").GetInt32() == id);
        Assert.All(pendingItems, x => Assert.Equal("NaoConsolidada", x.GetProperty("statusCirurgia").GetString()));

        using var billing = await ReadJsonAsync(await client.GetAsync("/api/faturamentos-medicos/?statusCirurgia=Consolidada"));
        Assert.All(billing.RootElement.GetProperty("items").EnumerateArray(),
            x => Assert.Equal("Consolidada", x.GetProperty("statusCirurgia").GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/pacientes/?statusCirurgia=999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/faturamentos-medicos/?statusCirurgia=999")).StatusCode);
    }

    [Fact]
    public async Task Status_ClinicBRecordsAreNeverReturnedOrInferredByClinicA()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        int remoteId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinica { Nome = "Other status clinic", Slug = "other-status-clinic" };
            db.Clinicas.Add(clinic);
            await db.SaveChangesAsync();
            var remote = Patient(clinic.Id, "remote-status", new DateTime(2026, 10, 1));
            db.Pacientes.Add(remote);
            await db.SaveChangesAsync();
            remoteId = remote.Id;
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/pacientes/{remoteId}")).StatusCode);
        foreach (var path in new[] { "/api/pacientes/", "/api/faturamentos-medicos/" })
        {
            using var list = await ReadJsonAsync(await client.GetAsync(path + "?statusCirurgia=Consolidada&clinicaId=2&pageSize=100"));
            Assert.DoesNotContain(list.RootElement.GetProperty("items").EnumerateArray(),
                x => x.GetProperty("id").GetInt32() == remoteId);
        }
    }

    [Theory]
    [InlineData("pacientes")]
    [InlineData("faturamentos-medicos")]
    public async Task Export_AppliesStatusFilter_AndRejectsInvalidStatus(string resource)
    {
        var queue = new CapturingFileExportQueue();
        using var factory = new HemodinksApiFactory(services =>
        {
            services.Remove(services.First(item => item.ServiceType == typeof(IFileExportQueue)));
            services.AddSingleton<IFileExportQueue>(queue);
        });
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinica { Nome = "Other export clinic", Slug = "other-status-export" };
            db.Clinicas.Add(clinic);
            await db.SaveChangesAsync();
            db.Pacientes.AddRange(
                Patient(1, "STATUS245-CONSOLIDATED", new DateTime(2026, 10, 1)),
                Patient(1, "STATUS245-PENDING", null),
                Patient(clinic.Id, "STATUS245-REMOTE", new DateTime(2026, 10, 1)));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/exports/", new
        {
            resource, format = "xlsx", reportType = "completo",
            filters = new { search = "STATUS245", statusCirurgia = "consolidada" }
        })).StatusCode);
        var message = Assert.Single(queue.Messages);
        Assert.Equal("Consolidada", message.Filters["statusCirurgia"]);
        var snapshot = message.Snapshot!;
        var nameColumn = Array.IndexOf(snapshot.Columns.ToArray(), "Paciente");
        Assert.Equal("STATUS245-CONSOLIDATED", Assert.Single(snapshot.Rows)[nameColumn]);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/exports/", new
        {
            resource, format = "xlsx", reportType = "completo", filters = new { statusCirurgia = "1" }
        })).StatusCode);
        Assert.Single(queue.Messages);
    }

    private static Paciente Patient(int clinic, string name, DateTime? consolidatedAt) => new()
    {
        ClinicaId = clinic, NomePaciente = name, DataAtendimento = consolidatedAt, Data = new DateTime(2026, 9, 1),
        User = new User
        {
            ClinicaId = clinic, Nome = name, Email = $"{Guid.NewGuid():N}@status.test", Senha = "fixture-only-hash",
            Telefone = "+5511999999911", PerfilId = Perfil.PacientesId, Ativo = true
        },
        FaturamentoMedico = new FaturamentoMedico { ClinicaId = clinic, HonorariosCirurgiao = 10m }
    };
}
