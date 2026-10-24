using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class PacienteCommandHandlerTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task DeletePaciente_SqlServer_EnforcesScopeAndCascadesOnlyAuthorizedPatient()
    {
        SqlServerTestConnection.RequireConfigured();

        var databaseName = $"HemodinksDeletionAudit_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlServerTestConnection.Create(databaseName)).Options;
        await using var seed = new PlatformDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var remoteClinic = new Clinica { Nome = "Remote", Slug = "remote-audit" };
            seed.Clinicas.Add(remoteClinic);
            await seed.SaveChangesAsync();
            var caller = DeletionUser("sql-caller", Perfil.MedicosId);
            var owner = DeletionUser("sql-owner", Perfil.MedicosId);
            var local = new Paciente { User = DeletionUser("sql-patient", Perfil.PacientesId),
                NomePaciente = "Local", MedicoUser = owner };
            var remoteUser = DeletionUser("sql-remote", Perfil.PacientesId);
            remoteUser.ClinicaId = remoteClinic.Id;
            var remote = new Paciente { ClinicaId = remoteClinic.Id, User = remoteUser, NomePaciente = "Remote" };
            local.Arquivos.Add(DeletionFile(1));
            remote.Arquivos.Add(DeletionFile(remoteClinic.Id));
            seed.Users.Add(caller);
            seed.Pacientes.AddRange(local, remote);
            await seed.SaveChangesAsync();

            var photos = new FakeProfilePhotoStorage();
            var files = new FakePatientFileStorage();
            await using (var scoped = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved()))
            {
                var handler = new DeletePacienteCommandHandler(scoped, photos, files,
                    NullLogger<DeletePacienteCommandHandler>.Instance);
                await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(new DeletePacienteCommand
                    { Id = local.Id, CurrentUserId = caller.Id, CurrentPerfilId = Perfil.MedicosId }, default));
                foreach (var profile in new[] { Perfil.MedicosId, Perfil.AdministradorId, Perfil.SuperAdministradorId })
                    await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(new DeletePacienteCommand
                        { Id = remote.Id, CurrentUserId = caller.Id, CurrentPerfilId = profile }, default));
            }
            Assert.Equal(0, photos.Deletions);
            Assert.Equal(0, files.Deletions);
            Assert.Equal(2, await seed.PacienteArquivos.CountAsync());

            var group = new GrupoMedico { Nome = "Authorized SQL group" };
            seed.GrupoMedicoUsuarios.AddRange(
                new GrupoMedicoUsuario { GrupoMedico = group, UserId = caller.Id },
                new GrupoMedicoUsuario { GrupoMedico = group, UserId = owner.Id });
            await seed.SaveChangesAsync();
            await using (var scoped = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved()))
            {
                var handler = new DeletePacienteCommandHandler(scoped, photos, files,
                    NullLogger<DeletePacienteCommandHandler>.Instance);
                await handler.Handle(new DeletePacienteCommand
                    { Id = local.Id, CurrentUserId = caller.Id, CurrentPerfilId = Perfil.MedicosId }, default);
            }
            seed.ChangeTracker.Clear();
            Assert.False(await seed.Users.AnyAsync(x => x.Id == local.UserId));
            Assert.False(await seed.Pacientes.AnyAsync(x => x.Id == local.Id));
            Assert.False(await seed.PacienteArquivos.AnyAsync(x => x.PacienteId == local.Id));
            Assert.True(await seed.Users.AnyAsync(x => x.Id == remote.UserId));
            Assert.True(await seed.PacienteArquivos.AnyAsync(x => x.PacienteId == remote.Id));
            Assert.Equal(1, photos.Deletions);
            Assert.Equal(1, files.Deletions);
        }
        finally
        {
            await seed.Database.EnsureDeletedAsync();
        }
    }
}
