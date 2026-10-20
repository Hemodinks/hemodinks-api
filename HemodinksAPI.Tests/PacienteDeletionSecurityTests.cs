using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class PacienteCommandHandlerTests
{
    [Theory]
    [InlineData(Perfil.MedicosId, false, false)]
    [InlineData(Perfil.MedicosId, true, false)]
    [InlineData(Perfil.MedicosId, false, true)]
    [InlineData(Perfil.AdministradorId, false, false)]
    [InlineData(Perfil.SuperAdministradorId, false, false)]
    public async Task DeletePaciente_EnforcesMedicalScopeBeforeDeletingStorage(int profile, bool related, bool sameGroup)
    {
        await using var db = TestDbContextFactory.Create();
        var owner = DeletionUser("owner", Perfil.MedicosId);
        var caller = DeletionUser("caller", profile);
        var patientUser = DeletionUser("patient", Perfil.PacientesId);
        patientUser.FotoPerfil = "private-photo";
        db.Users.AddRange(owner, caller, patientUser);
        await db.SaveChangesAsync();
        var patient = new Paciente { UserId = patientUser.Id, NomePaciente = "Private patient",
            MedicoUserId = related ? caller.Id : owner.Id };
        patient.Arquivos.Add(DeletionFile(Clinica.DefaultId));
        db.Pacientes.Add(patient);
        if (sameGroup)
        {
            var group = new GrupoMedico { Nome = "Shared group" };
            db.GrupoMedicoUsuarios.AddRange(
                new GrupoMedicoUsuario { GrupoMedico = group, UserId = caller.Id },
                new GrupoMedicoUsuario { GrupoMedico = group, UserId = owner.Id });
        }
        await db.SaveChangesAsync();
        var photoStorage = new FakeProfilePhotoStorage();
        var fileStorage = new FakePatientFileStorage();
        var handler = new DeletePacienteCommandHandler(db, photoStorage, fileStorage,
            NullLogger<DeletePacienteCommandHandler>.Instance);
        var command = new DeletePacienteCommand { Id = patient.Id, CurrentUserId = caller.Id, CurrentPerfilId = profile };
        if (profile == Perfil.MedicosId && !related && !sameGroup)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(command, default));
            Assert.True(await db.Users.AnyAsync(x => x.Id == patientUser.Id));
            Assert.True(await db.Pacientes.AnyAsync(x => x.Id == patient.Id));
            Assert.Equal("private-photo", (await db.Users.SingleAsync(x => x.Id == patientUser.Id)).FotoPerfil);
            Assert.True(await db.PacienteArquivos.AnyAsync(x => x.PacienteId == patient.Id));
            Assert.Equal(0, photoStorage.Deletions);
            Assert.Equal(0, fileStorage.Deletions);
        }
        else
        {
            await handler.Handle(command, default);
            Assert.False(await db.Users.AnyAsync(x => x.Id == patientUser.Id));
            Assert.Equal(1, photoStorage.Deletions);
            Assert.Equal(1, fileStorage.Deletions);
        }
    }

    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.SuperAdministradorId)]
    [InlineData(Perfil.MedicosId)]
    public async Task DeletePaciente_OtherClinic_DoesNotTouchDataOrStorage(int profile)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        int patientId, userId;
        await using (var seed = new PlatformDbContext(options))
        {
            var user = DeletionUser("remote", Perfil.PacientesId);
            user.ClinicaId = 2;
            user.FotoPerfil = "private-photo";
            var patient = new Paciente { ClinicaId = 2, User = user, NomePaciente = "Remote patient" };
            patient.Arquivos.Add(DeletionFile(2));
            seed.Pacientes.Add(patient);
            await seed.SaveChangesAsync();
            patientId = patient.Id; userId = user.Id;
        }
        await using var db = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());
        var photos = new FakeProfilePhotoStorage();
        var files = new FakePatientFileStorage();
        var handler = new DeletePacienteCommandHandler(db, photos, files, NullLogger<DeletePacienteCommandHandler>.Instance);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(new DeletePacienteCommand
            { Id = patientId, CurrentUserId = 999, CurrentPerfilId = profile }, default));
        Assert.True(await db.Pacientes.IgnoreQueryFilters().AnyAsync(x => x.Id == patientId && x.ClinicaId == 2));
        Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == userId && x.ClinicaId == 2));
        Assert.True(await db.PacienteArquivos.IgnoreQueryFilters().AnyAsync(x => x.PacienteId == patientId && x.ClinicaId == 2));
        Assert.Equal(0, photos.Deletions);
        Assert.Equal(0, files.Deletions);
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
