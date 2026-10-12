using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Application.Async;
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
    public async Task TenantAudit_ById_DoesNotRevealOrMutateOtherClinic(bool superAdmin)
    {
        using var factory = new HemodinksApiFactory();
        var beta = await SeedClinicaBetaAsync(factory);
        int userId, patientId, groupId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ownerClinic = superAdmin ? beta.Id : Clinica.DefaultId;
            var user = AuditPatientUser(ownerClinic);
            var patient = new Paciente { ClinicaId = ownerClinic, User = user, NomePaciente = user.Nome };
            var group = new GrupoMedico { ClinicaId = ownerClinic, Nome = "Tenant audit group" };
            db.Pacientes.Add(patient);
            db.GruposMedicos.Add(group);
            await db.SaveChangesAsync();
            userId = user.Id; patientId = patient.Id; groupId = group.Id;
        }

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, superAdmin ? Clinica.DefaultSlug : beta.Slug,
            beta.AdminEmail, beta.AdminPassword);
        foreach (var path in new[] { $"/api/users/{userId}", $"/api/pacientes/{patientId}" })
        {
            using var response = await client.GetAsync(path);
            using var missing = await client.GetAsync(path[..(path.LastIndexOf('/') + 1)] + int.MaxValue);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(missing.StatusCode, response.StatusCode);
            Assert.Equal(await missing.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        }
        foreach (var path in new[] { $"/api/users/{userId}", $"/api/pacientes/{patientId}", $"/api/grupos-medicos/{groupId}" })
        {
            using var response = await client.DeleteAsync(path);
            using var missing = await client.DeleteAsync(path[..(path.LastIndexOf('/') + 1)] + int.MaxValue);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(missing.StatusCode, response.StatusCode);
            Assert.Equal(await missing.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        }
        using var checkScope = factory.Services.CreateScope();
        checkScope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var check = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await check.Users.AnyAsync(x => x.Id == userId && x.Ativo));
        Assert.True(await check.Pacientes.AnyAsync(x => x.Id == patientId));
        Assert.True(await check.GruposMedicos.AnyAsync(x => x.Id == groupId));
    }

    [Fact]
    public async Task TenantAudit_DoctorCannotDeletePatientOutsideMedicalScope()
    {
        using var factory = new HemodinksApiFactory();
        var beta = await SeedClinicaBetaAsync(factory);
        int patientId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = AuditPatientUser(beta.Id);
            var patient = new Paciente { ClinicaId = beta.Id, User = user, NomePaciente = user.Nome };
            db.Pacientes.Add(patient);
            await db.SaveChangesAsync();
            patientId = patient.Id;
        }
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, beta.Slug, "dra.beta@hemodinks.com", TestPasswords.Valid);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/pacientes/{patientId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/pacientes/{patientId}")).StatusCode);
        using var checkScope = factory.Services.CreateScope();
        checkScope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        Assert.True(await checkScope.ServiceProvider.GetRequiredService<AppDbContext>().Pacientes.AnyAsync(x => x.Id == patientId));
    }

    [Fact]
    public async Task TenantAudit_ExportUsesAuthenticatedClinicDespiteClientHints()
    {
        var queue = new CapturingFileExportQueue();
        using var factory = new HemodinksApiFactory(services =>
        {
            foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IFileExportQueue)).ToList())
                services.Remove(descriptor);
            services.AddSingleton<IFileExportQueue>(queue);
        });
        var beta = await SeedClinicaBetaAsync(factory);
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        client.DefaultRequestHeaders.Remove("X-Clinica-Slug");
        client.DefaultRequestHeaders.Add("X-Clinica-Slug", Clinica.DefaultSlug);
        var response = await client.PostAsJsonAsync("/api/exports/", new
        {
            resource = "pacientes", format = "xlsx", clinicaId = Clinica.DefaultId,
            filters = new Dictionary<string, string?> { ["ClinicaId"] = "1" }
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(beta.Id, Assert.Single(queue.Messages).ClinicaId);
    }

    [Fact]
    public async Task TenantAudit_GroupCreationAndEditing_RejectCrossClinicIds()
    {
        using var factory = new HemodinksApiFactory();
        var beta = await SeedClinicaBetaAsync(factory);
        int remoteDoctorId, localDoctorId, remoteGroupId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var remoteDoctor = AuditPatientUser(Clinica.DefaultId);
            remoteDoctor.PerfilId = Perfil.MedicosId;
            db.Users.Add(remoteDoctor);
            var remoteGroup = new GrupoMedico { ClinicaId = Clinica.DefaultId, Nome = "Remote audit group" };
            db.GruposMedicos.Add(remoteGroup);
            await db.SaveChangesAsync();
            remoteDoctorId = remoteDoctor.Id;
            remoteGroupId = remoteGroup.Id;
            localDoctorId = await db.Users.Where(x => x.ClinicaId == beta.Id && x.PerfilId == Perfil.MedicosId)
                .Select(x => x.Id).SingleAsync();
        }
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        var rejected = await client.PostAsJsonAsync("/api/grupos-medicos/", new
            { nome = "Invalid remote member", medicoUserIds = new[] { remoteDoctorId } });
        var missing = await client.PostAsJsonAsync("/api/grupos-medicos/", new
            { nome = "Invalid remote member", medicoUserIds = new[] { int.MaxValue } });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await rejected.Content.ReadAsStringAsync());
        var valid = await client.PostAsJsonAsync("/api/grupos-medicos/", new
            { nome = "Local audit group", medicoUserIds = new[] { localDoctorId }, clinicaId = Clinica.DefaultId });
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        using var payload = await ReadJsonAsync(valid);
        var groupId = payload.RootElement.GetProperty("id").GetInt32();
        var body = new { nome = "Changed remote group", medicoUserIds = new[] { localDoctorId }, ativo = true };
        var update = await client.PutAsJsonAsync($"/api/grupos-medicos/{remoteGroupId}", body);
        var missingUpdate = await client.PutAsJsonAsync($"/api/grupos-medicos/{int.MaxValue}", body);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(await missingUpdate.Content.ReadAsStringAsync(), await update.Content.ReadAsStringAsync());
        using var checkScope = factory.Services.CreateScope();
        checkScope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var check = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(beta.Id, (await check.GruposMedicos.SingleAsync(x => x.Id == groupId)).ClinicaId);
        Assert.Equal("Remote audit group", (await check.GruposMedicos.SingleAsync(x => x.Id == remoteGroupId)).Nome);
        Assert.False(await check.GruposMedicos.AnyAsync(x => x.Nome == "Invalid remote member"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TenantAudit_LoginDiscovery_DoesNotRevealRevokedIdentity(bool globalInactive)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var membership = await db.UsuariosClinicas.Include(x => x.UsuarioGlobal)
                .SingleAsync(x => x.User.Email == "gmarcone@gmail.com" && x.ClinicaId == Clinica.DefaultId);
            if (globalInactive) membership.UsuarioGlobal.Ativo = false;
            else membership.Ativo = false;
            await db.SaveChangesAsync();
        }
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/users/login-context", new
            { email = "gmarcone@gmail.com", senha = TestPasswords.Valid });
        var unknown = await anonymous.PostAsJsonAsync("/api/users/login-context", new
            { email = "unknown-audit@example.com", senha = TestPasswords.Valid });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("clinicaId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static User AuditPatientUser(int clinicId) => new()
    {
        ClinicaId = clinicId, Nome = "Tenant audit patient", Email = $"{Guid.NewGuid():N}@example.com",
        Telefone = "+5511999999903", Senha = "unused-hash", PerfilId = Perfil.PacientesId,
        Ativo = true, PrecisaTrocarSenha = false
    };
}
