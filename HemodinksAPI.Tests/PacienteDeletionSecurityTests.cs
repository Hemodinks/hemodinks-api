using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using HemodinksAPI.Domain.Exceptions;

namespace HemodinksAPI.Tests;

// #369: DELETE de paciente so inativa; nada de linha, arquivo ou foto e removido.
public partial class PacienteCommandHandlerTests
{
    [Theory]
    [InlineData(Perfil.MedicosId)]
    [InlineData(Perfil.ControllerId)]
    [InlineData(Perfil.EquipeId)]
    [InlineData(Perfil.PacientesId)]
    public async Task InativarPaciente_PerfilNaoAdministrador_E_Negado_SemAlterarNada(int profile)
    {
        await using var db = TestDbContextFactory.Create();
        var (patient, patientUser) = await SeedDeletionPatientAsync(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DeletePacienteCommandHandler(db)
            .Handle(new DeletePacienteCommand { Id = patient.Id, CurrentUserId = 1, CurrentPerfilId = profile }, default));

        db.ChangeTracker.Clear();
        Assert.True((await db.Pacientes.SingleAsync(x => x.Id == patient.Id)).Ativo);
        Assert.Equal("private-photo", (await db.Users.SingleAsync(x => x.Id == patientUser.Id)).FotoPerfil);
    }

    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.SuperAdministradorId)]
    public async Task InativarPaciente_Administrador_PreservaProntuarioArquivosEFoto_E_ReativacaoRestaura(int profile)
    {
        await using var db = TestDbContextFactory.Create();
        var (patient, patientUser) = await SeedDeletionPatientAsync(db);
        var now = new DateTime(2026, 11, 23, 15, 0, 0, DateTimeKind.Utc);

        await new DeletePacienteCommandHandler(db, new FixedTime(now))
            .Handle(new DeletePacienteCommand { Id = patient.Id, CurrentUserId = 77, CurrentPerfilId = profile, Motivo = "Transferido" }, default);
        // Idempotente: inativar de novo nao altera data nem autor.
        await new DeletePacienteCommandHandler(db, new FixedTime(now.AddDays(1)))
            .Handle(new DeletePacienteCommand { Id = patient.Id, CurrentUserId = 88, CurrentPerfilId = profile, Motivo = "Transferido" }, default);

        db.ChangeTracker.Clear();
        var inactive = await db.Pacientes.SingleAsync(x => x.Id == patient.Id);
        Assert.False(inactive.Ativo);
        Assert.Equal(now, inactive.InativadoEm);
        Assert.Equal(77, inactive.InativadoPorUserId);
        Assert.True(await db.Users.AnyAsync(x => x.Id == patientUser.Id && x.FotoPerfil == "private-photo"));
        Assert.True(await db.PacienteArquivos.AnyAsync(x => x.PacienteId == patient.Id));

        await new ReativarPacienteCommandHandler(db)
            .Handle(new ReativarPacienteCommand { Id = patient.Id, CurrentPerfilId = profile, Motivo = "Retorno" }, default);
        db.ChangeTracker.Clear();
        var reactivated = await db.Pacientes.SingleAsync(x => x.Id == patient.Id);
        Assert.True(reactivated.Ativo);
        Assert.Null(reactivated.InativadoEm);
        Assert.Null(reactivated.InativadoPorUserId);
    }

    [Theory]
    [InlineData(Perfil.MedicosId)]
    [InlineData(Perfil.ControllerId)]
    public async Task ReativarPaciente_PerfilNaoAdministrador_E_Negado(int profile)
    {
        await using var db = TestDbContextFactory.Create();
        var (patient, _) = await SeedDeletionPatientAsync(db);
        patient.Ativo = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ReativarPacienteCommandHandler(db)
            .Handle(new ReativarPacienteCommand { Id = patient.Id, CurrentPerfilId = profile }, default));
        db.ChangeTracker.Clear();
        Assert.False((await db.Pacientes.SingleAsync(x => x.Id == patient.Id)).Ativo);
    }

    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.SuperAdministradorId)]
    public async Task InativarEReativar_PacienteDeOutraClinica_404_SemAlterar(int profile)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        int activeId, inactiveId;
        await using (var seed = new PlatformDbContext(options))
        {
            var activeUser = DeletionUser("remote-active", Perfil.PacientesId);
            activeUser.ClinicaId = 2;
            var inactiveUser = DeletionUser("remote-inactive", Perfil.PacientesId);
            inactiveUser.ClinicaId = 2;
            var active = new Paciente { ClinicaId = 2, User = activeUser, NomePaciente = "Remote active" };
            var inactive = new Paciente { ClinicaId = 2, User = inactiveUser, NomePaciente = "Remote inactive", Ativo = false };
            active.Arquivos.Add(DeletionFile(2));
            seed.Pacientes.AddRange(active, inactive);
            await seed.SaveChangesAsync();
            activeId = active.Id; inactiveId = inactive.Id;
        }

        await using (var db = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved()))
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => new DeletePacienteCommandHandler(db).Handle(
                new DeletePacienteCommand { Id = activeId, CurrentUserId = 999, CurrentPerfilId = profile }, default));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => new ReativarPacienteCommandHandler(db).Handle(
                new ReativarPacienteCommand { Id = inactiveId, CurrentPerfilId = profile }, default));
        }

        await using var check = new PlatformDbContext(options);
        Assert.True(await check.Pacientes.AnyAsync(x => x.Id == activeId && x.Ativo && x.InativadoEm == null));
        Assert.True(await check.Pacientes.AnyAsync(x => x.Id == inactiveId && !x.Ativo));
        Assert.True(await check.PacienteArquivos.AnyAsync(x => x.PacienteId == activeId));
    }

    private static async Task<(Paciente Patient, User PatientUser)> SeedDeletionPatientAsync(AppDbContext db)
    {
        var owner = DeletionUser("owner", Perfil.MedicosId);
        var patientUser = DeletionUser("patient", Perfil.PacientesId);
        patientUser.FotoPerfil = "private-photo";
        db.Users.AddRange(owner, patientUser);
        await db.SaveChangesAsync();
        var patient = new Paciente { UserId = patientUser.Id, NomePaciente = "Private patient", MedicoUserId = owner.Id };
        patient.Arquivos.Add(DeletionFile(Clinica.DefaultId));
        db.Pacientes.Add(patient);
        await db.SaveChangesAsync();
        return (patient, patientUser);
    }

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private static PacienteArquivo DeletionFile(int clinicId) => new()
    {
        ClinicaId = clinicId, NomeOriginal = "private.pdf", ContentType = "application/pdf",
        Url = "private-file", TamanhoBytes = 100
    };

    private static User DeletionUser(string name, int profile) => new()
    {
        Nome = name, Email = $"{name}-{Guid.NewGuid():N}@example.com", Telefone = "11999999999",
        Senha = "unused-hash", PerfilId = profile, Ativo = true
    };
}
