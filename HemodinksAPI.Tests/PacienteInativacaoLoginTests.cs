using System.Net;
using System.Net.Http.Headers;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

// #369 (decisao de produto): inativar o paciente desliga o login do titular; reativar religa
// so o que a propria inativacao desligou.
public sealed class PacienteInativacaoLoginTests : ApiEndpointTestBase
{
    [Fact]
    public async Task Inativar_DerrubaTokenDoTitular_E_Reativar_DevolveAcesso()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var (patientId, token) = await SeedPatientWithLoginAsync(factory, userActive: true);

        using var titular = factory.CreateClient();
        titular.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await titular.GetAsync($"/api/pacientes/{patientId}")).StatusCode);

        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/pacientes/{patientId}")).StatusCode);

        // O mesmo token, ja emitido, deixa de valer: o vinculo inativo e checado a cada requisicao.
        var blocked = await titular.GetAsync($"/api/pacientes/{patientId}");
        Assert.True(blocked.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, blocked.StatusCode.ToString());
        var (user, membership, patient) = await ReadStateAsync(factory, patientId);
        Assert.False(user.Ativo);
        Assert.False(membership.Ativo);
        Assert.True(patient.LoginBloqueadoNaInativacao);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/pacientes/{patientId}/reativacao", null)).StatusCode);
        (user, membership, patient) = await ReadStateAsync(factory, patientId);
        Assert.True(user.Ativo);
        Assert.True(membership.Ativo);
        Assert.False(patient.LoginBloqueadoNaInativacao);
        Assert.Equal(HttpStatusCode.OK, (await titular.GetAsync($"/api/pacientes/{patientId}")).StatusCode);
    }

    [Fact]
    public async Task Reativar_NaoReligaLoginQueJaEstavaDesativadoAntes()
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var (patientId, _) = await SeedPatientWithLoginAsync(factory, userActive: false);

        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/pacientes/{patientId}")).StatusCode);
        var (_, _, inactive) = await ReadStateAsync(factory, patientId);
        Assert.False(inactive.LoginBloqueadoNaInativacao);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/pacientes/{patientId}/reativacao", null)).StatusCode);
        var (user, membership, patient) = await ReadStateAsync(factory, patientId);
        Assert.True(patient.Ativo);
        Assert.False(user.Ativo);
        Assert.False(membership.Ativo);
    }

    [Fact]
    public async Task Inativar_NaoDesligaLoginDeUsuarioQueNaoEPaciente()
    {
        await using var db = TestDbContextFactory.Create();
        var staff = new User
        {
            Nome = "Usuario equipe vinculado", Email = $"staff-{Guid.NewGuid():N}@example.com", Telefone = "11999999999",
            Senha = "unused-hash", PerfilId = Perfil.MedicosId, Ativo = true
        };
        db.Users.Add(staff);
        await db.SaveChangesAsync();
        var patient = new Paciente { UserId = staff.Id, NomePaciente = "Cadastro anomalo" };
        db.Pacientes.Add(patient);
        await db.SaveChangesAsync();

        await new DeletePacienteCommandHandler(db).Handle(
            new DeletePacienteCommand { Id = patient.Id, CurrentUserId = 1, CurrentPerfilId = Perfil.AdministradorId }, default);

        db.ChangeTracker.Clear();
        Assert.True((await db.Users.SingleAsync(u => u.Id == staff.Id)).Ativo);
        Assert.False((await db.Pacientes.SingleAsync(p => p.Id == patient.Id)).LoginBloqueadoNaInativacao);
    }

    private static async Task<(int PatientId, string Token)> SeedPatientWithLoginAsync(HemodinksApiFactory factory, bool userActive)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            ClinicaId = Clinica.DefaultId, Nome = "Titular Inativacao", Email = $"titular-{Guid.NewGuid():N}@example.com",
            Telefone = "+5511966660000", Senha = "hash-nao-utilizado", DataNascimento = new DateTime(1990, 1, 1),
            DataCadastro = DateTime.UtcNow, Ativo = true, PrecisaTrocarSenha = false, PerfilId = Perfil.PacientesId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.Entry(user).Reference(item => item.Perfil).LoadAsync();
        await db.Entry(user).Reference(item => item.Clinica).LoadAsync();
        var membership = await GlobalIdentityService.EnsureForUserAsync(db, user, CancellationToken.None);
        var patient = new Paciente { ClinicaId = Clinica.DefaultId, UserId = user.Id, NomePaciente = user.Nome };
        db.Pacientes.Add(patient);
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .GenerateToken(membership.UsuarioGlobal, membership, user);
        if (!userActive)
        {
            user.Ativo = false;
            await GlobalIdentityService.SynchronizeUserAsync(db, user, CancellationToken.None);
            await db.SaveChangesAsync();
        }
        return (patient.Id, token);
    }

    private static async Task<(User User, UsuarioClinica Membership, Paciente Patient)> ReadStateAsync(
        HemodinksApiFactory factory, int patientId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var patient = await db.Pacientes.AsNoTracking().SingleAsync(p => p.Id == patientId);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == patient.UserId);
        var membership = await db.UsuariosClinicas.AsNoTracking().SingleAsync(m => m.UserId == user.Id);
        return (user, membership, patient);
    }
}
