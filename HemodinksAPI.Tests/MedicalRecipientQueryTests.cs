using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Features.Pacientes.Observacoes;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class MedicalRecipientQueryTests
{
    [Fact]
    public async Task MedicalRecipients_TranslateToSql_AndIncludeOnlyActiveStaffInCurrentClinic()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var context = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());

        // Use production mappings and tenant filters with the columns these queries read.
        // Avoid unrelated SQL Server-specific defaults in the full database schema.
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Users (Id INTEGER PRIMARY KEY, ClinicaId INTEGER, PerfilId INTEGER,
                Ativo INTEGER, Nome TEXT, Email TEXT);
            CREATE TABLE Perfis (Id INTEGER PRIMARY KEY, Nome TEXT);
            CREATE TABLE GruposMedicos (Id INTEGER PRIMARY KEY, ClinicaId INTEGER, Ativo INTEGER, Nome TEXT);
            CREATE TABLE GrupoMedicoUsuarios (GrupoMedicoId INTEGER, UserId INTEGER, ClinicaId INTEGER);
            INSERT INTO Perfis VALUES (1, 'Administrador'), (2, 'Medico'), (3, 'Paciente'),
                (4, 'Controller'), (5, 'Super administrador'), (6, 'Equipe');
            INSERT INTO Users VALUES
                (1, 1, 1, 1, 'Admin', 'admin@example.com'),
                (2, 1, 5, 1, 'Super', 'super@example.com'),
                (3, 1, 4, 1, 'Controller', 'controller@example.com'),
                (4, 1, 2, 1, 'Medico', 'medico@example.com'),
                (5, 1, 3, 1, 'Paciente', 'paciente@example.com'),
                (6, 1, 6, 1, 'Equipe', 'equipe@example.com'),
                (7, 1, 1, 0, 'Inativo', 'inactive@example.com'),
                (8, 2, 1, 1, 'Outra clinica', 'other@example.com'),
                (9, 1, 5, 0, 'Super inativo', 'inactive-super@example.com'),
                (10, 1, 4, 0, 'Controller inativo', 'inactive-controller@example.com');
            """);

        var doctor = new CurrentUserContext(4, Perfil.MedicosId, "Medico");
        var observationRecipients = await PacienteObservacaoRecipients.ResolveRootRecipientsAsync(
            context,
            new CreatePacienteObservacaoCommand { CurrentUserId = doctor.Id, CurrentPerfilId = doctor.PerfilId },
            new PacienteObservacaoContext(1, 1, "Paciente", doctor.Id, doctor.Nome, null, null, null, null),
            CancellationToken.None);
        Assert.Equal(new[] { 1, 2, 3 }, observationRecipients.Order());

        var eventRecipients = EventFeatureRules.BuildAllowedNotificationRecipientUserIds(context, doctor);
        Assert.Equal(new[] { 1, 2, 3 }, eventRecipients.Order());

        var agendaOptions = await new GetAgendaNotificationRecipientOptionsQueryHandler(context).Handle(
            new GetAgendaNotificationRecipientOptionsQuery { CurrentUser = doctor }, CancellationToken.None);
        Assert.Equal(new[] { 1, 2, 3 }, agendaOptions.Users.Select(user => user.Id).Order());
        Assert.Empty(agendaOptions.Groups);
    }
}
