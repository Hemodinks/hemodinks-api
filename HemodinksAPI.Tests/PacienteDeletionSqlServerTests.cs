using HemodinksAPI.Domain.Exceptions;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public partial class PacienteCommandHandlerTests
{
    // #369: no SQL Server real, inativar nao apaga linha nem arquivo e nao alcanca outra clinica.
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task InativarPaciente_SqlServer_PreservaDadosERespeitaTenant()
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
            var local = new Paciente { User = DeletionUser("sql-patient", Perfil.PacientesId), NomePaciente = "Local" };
            var remoteUser = DeletionUser("sql-remote", Perfil.PacientesId);
            remoteUser.ClinicaId = remoteClinic.Id;
            var remote = new Paciente { ClinicaId = remoteClinic.Id, User = remoteUser, NomePaciente = "Remote" };
            local.Arquivos.Add(DeletionFile(1));
            remote.Arquivos.Add(DeletionFile(remoteClinic.Id));
            seed.Users.Add(caller);
            seed.Pacientes.AddRange(local, remote);
            await seed.SaveChangesAsync();

            await using (var scoped = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved()))
            {
                var handler = new DeletePacienteCommandHandler(scoped);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new DeletePacienteCommand
                    { Id = local.Id, CurrentUserId = caller.Id, CurrentPerfilId = Perfil.MedicosId }, default));
                foreach (var profile in new[] { Perfil.AdministradorId, Perfil.SuperAdministradorId })
                    await Assert.ThrowsAsync<ResourceNotFoundException>(() => handler.Handle(new DeletePacienteCommand
                        { Id = remote.Id, CurrentUserId = caller.Id, CurrentPerfilId = profile }, default));
                await handler.Handle(new DeletePacienteCommand
                    { Id = local.Id, CurrentUserId = caller.Id, CurrentPerfilId = Perfil.AdministradorId }, default);
            }

            seed.ChangeTracker.Clear();
            Assert.True(await seed.Users.AnyAsync(x => x.Id == local.UserId));
            Assert.True(await seed.Pacientes.AnyAsync(x => x.Id == local.Id && !x.Ativo && x.InativadoPorUserId == caller.Id));
            Assert.True(await seed.PacienteArquivos.AnyAsync(x => x.PacienteId == local.Id));
            Assert.True(await seed.Pacientes.AnyAsync(x => x.Id == remote.Id && x.Ativo));
            Assert.Equal(2, await seed.PacienteArquivos.CountAsync());
        }
        finally
        {
            await seed.Database.EnsureDeletedAsync();
        }
    }
}
