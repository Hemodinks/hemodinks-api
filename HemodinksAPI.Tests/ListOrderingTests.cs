using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Features.Users.Queries;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class ListOrderingTests
{
    [Fact]
    public async Task GetAllUsers_OrdersByLatestRecordActivityThenName()
    {
        await using var context = TestDbContextFactory.Create();
        context.Users.AddRange(
            CreateUser("Carlos Antigo", "carlos@hemodinks.com", "52998224725", new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 21, 9, 0, 0, DateTimeKind.Utc)),
            CreateUser("Bruno Recente", "bruno@hemodinks.com", "11144477735", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc)),
            CreateUser("Ana Recente", "ana@hemodinks.com", "93541134780", new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc)),
            CreateUser("Paciente Oculto", "paciente.oculto@hemodinks.com", "39053344705", new DateTime(2026, 6, 4, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc), Perfil.PacientesId));
        await context.SaveChangesAsync();

        var handler = new GetAllUsersQueryHandler(context, NullLogger<GetAllUsersQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllUsersQuery { Page = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal(["Ana Recente", "Bruno Recente", "Carlos Antigo"], result.Items.Select(user => user.Nome));

        var patientProfileResult = await handler.Handle(new GetAllUsersQuery { Page = 1, PageSize = 10, ProfileId = Perfil.PacientesId }, CancellationToken.None);

        Assert.Empty(patientProfileResult.Items);
        Assert.Equal(0, patientProfileResult.TotalItems);
    }

    [Fact]
    public async Task GetAllPacientes_OrdersByLatestLinkedUserActivityThenName()
    {
        await using var context = TestDbContextFactory.Create();
        var antigo = CreateUser("Zelia Antiga", "zelia@hemodinks.com", "52998224725", new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);
        var bruno = CreateUser("Bruno Recente", "bruno.paciente@hemodinks.com", "11144477735", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc), Perfil.PacientesId);
        var ana = CreateUser("Ana Recente", "ana.paciente@hemodinks.com", "93541134780", new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc), Perfil.PacientesId);

        context.Pacientes.AddRange(
            new Paciente { User = antigo, NomePaciente = antigo.Nome },
            new Paciente { User = bruno, NomePaciente = bruno.Nome },
            new Paciente { User = ana, NomePaciente = ana.Nome });
        await context.SaveChangesAsync();

        var handler = new GetAllPacientesQueryHandler(context, NullLogger<GetAllPacientesQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllPacientesQuery
        {
            Page = 1,
            PageSize = 10,
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        Assert.Equal(["Ana Recente", "Bruno Recente", "Zelia Antiga"], result.Items.Select(paciente => paciente.NomePaciente));
    }

    [Fact]
    public async Task GetAllPacientes_FiltersAdminPatientsByMedicoConvenioAndProcedimento()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = CreateUser("Dra. Ana", "dra.ana@hemodinks.com", "39053344705", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null);
        var matching = CreateUser("Paciente Match", "match@hemodinks.com", "52998224725", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);
        var wrongConvenio = CreateUser("Paciente Convenio", "convenio@hemodinks.com", "11144477735", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);
        var wrongProcedimento = CreateUser("Paciente Procedimento", "procedimento@hemodinks.com", "93541134780", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);

        context.Pacientes.AddRange(
            new Paciente
            {
                User = matching,
                NomePaciente = matching.Nome,
                MedicoUser = doctor,
                Medico = doctor.Nome,
                Convenio = "Particular",
                Procedimentos =
                [
                    new PacienteProcedimento { Procedimento = "Consulta", Ordem = 1 }
                ],
            },
            new Paciente
            {
                User = wrongConvenio,
                NomePaciente = wrongConvenio.Nome,
                MedicoUser = doctor,
                Medico = doctor.Nome,
                Convenio = "Unimed",
                Procedimento = "Consulta",
            },
            new Paciente
            {
                User = wrongProcedimento,
                NomePaciente = wrongProcedimento.Nome,
                MedicoUser = doctor,
                Medico = doctor.Nome,
                Convenio = "Particular",
                Procedimento = "Retorno",
            });
        await context.SaveChangesAsync();

        var handler = new GetAllPacientesQueryHandler(context, NullLogger<GetAllPacientesQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllPacientesQuery
        {
            Page = 1,
            PageSize = 10,
            Medico = "Ana",
            Convenio = "Particular",
            Procedimento = "Consulta",
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        Assert.Equal(["Paciente Match"], result.Items.Select(paciente => paciente.NomePaciente));
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task GetAllPacientes_FiltersMultipleDoctorsConveniosAndInclusiveProcedureDates()
    {
        await using var context = TestDbContextFactory.Create();
        var firstDoctor = CreateUser("Dra. Ana", "ana.filtro@hemodinks.com", "39053344705", DateTime.UtcNow, null);
        var secondDoctor = CreateUser("Dr. Bruno", "bruno.filtro@hemodinks.com", "98765432100", DateTime.UtcNow, null);
        var outsideDoctor = CreateUser("Dra. Carla", "carla.filtro@hemodinks.com", "93541134780", DateTime.UtcNow, null);
        var selectedConvenio = new Convenio { DescricaoConvenio = "Selecionado" };
        var outsideConvenio = new Convenio { DescricaoConvenio = "Fora do filtro" };

        Paciente CreatePaciente(string nome, string email, string cpf, DateTime data, User doctor, Convenio convenio) => new()
        {
            User = CreateUser(nome, email, cpf, DateTime.UtcNow, null, Perfil.PacientesId),
            NomePaciente = nome,
            Data = data,
            MedicoUser = doctor,
            Medico = doctor.Nome,
            ConvenioReferencia = convenio,
            Convenio = convenio.DescricaoConvenio,
        };

        context.Pacientes.AddRange(
            CreatePaciente("Limite inicial", "inicio@hemodinks.com", "52998224725", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), firstDoctor, selectedConvenio),
            CreatePaciente("Limite final", "fim@hemodinks.com", "11144477735", new DateTime(2026, 6, 30, 23, 59, 0, DateTimeKind.Utc), secondDoctor, selectedConvenio),
            CreatePaciente("Médico de fora", "medico.fora@hemodinks.com", "86288366757", new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc), outsideDoctor, selectedConvenio),
            CreatePaciente("Convênio de fora", "convenio.fora@hemodinks.com", "15350946056", new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc), firstDoctor, outsideConvenio),
            CreatePaciente("Data de fora", "data.fora@hemodinks.com", "01400233007", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), firstDoctor, selectedConvenio));
        await context.SaveChangesAsync();

        var handler = new GetAllPacientesQueryHandler(context, NullLogger<GetAllPacientesQueryHandler>.Instance);
        var result = await handler.Handle(new GetAllPacientesQuery
        {
            Page = 1,
            PageSize = 10,
            MedicoUserIds = $"{firstDoctor.Id},{secondDoctor.Id}",
            ConvenioIds = selectedConvenio.IdConvenio.ToString(),
            DataInicio = new DateTime(2026, 6, 1),
            DataFinal = new DateTime(2026, 6, 30),
            CurrentPerfilId = Perfil.AdministradorId,
        }, CancellationToken.None);

        Assert.Equal(2, result.TotalItems);
        Assert.Equal(["Limite final", "Limite inicial"], result.Items.Select(paciente => paciente.NomePaciente));
    }

    [Fact]
    public async Task GetAllPacientes_WhenLoggedDoctor_ReturnsOnlyPatientsLinkedToDoctorUserId()
    {
        await using var context = TestDbContextFactory.Create();
        const string doctorName = "Dr. George";
        var doctor = CreateUser(doctorName, "dr.george@hemodinks.com", "39053344705", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null);
        var otherDoctorUser = CreateUser("Dra. Ana", "dra.ana@hemodinks.com", "98765432100", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null);
        var linked = CreateUser("Paciente Vinculado", "vinculado@hemodinks.com", "52998224725", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);
        var otherDoctor = CreateUser("Paciente Outro Medico", "outro.medico@hemodinks.com", "11144477735", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);
        var withoutDoctor = CreateUser("Paciente Sem Medico", "sem.medico@hemodinks.com", "93541134780", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);

        context.Pacientes.AddRange(
            new Paciente
            {
                User = linked,
                NomePaciente = linked.Nome,
                MedicoUser = doctor,
                Medico = doctorName,
            },
            new Paciente
            {
                User = otherDoctor,
                NomePaciente = otherDoctor.Nome,
                MedicoUser = otherDoctorUser,
                Medico = otherDoctorUser.Nome,
            },
            new Paciente
            {
                User = withoutDoctor,
                NomePaciente = withoutDoctor.Nome,
                Medico = null,
            });
        await context.SaveChangesAsync();

        var handler = new GetAllPacientesQueryHandler(context, NullLogger<GetAllPacientesQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllPacientesQuery
        {
            Page = 1,
            PageSize = 10,
            CurrentPerfilId = Perfil.MedicosId,
            CurrentUserId = doctor.Id
        }, CancellationToken.None);

        Assert.Equal(["Paciente Vinculado"], result.Items.Select(paciente => paciente.NomePaciente));
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task GetAllPacientes_WhenLoggedPatient_ReturnsOnlyOwnMedicalRecord()
    {
        await using var context = TestDbContextFactory.Create();
        var currentUser = CreateUser("Paciente Atual", "atual@hemodinks.com", "52998224725", DateTime.UtcNow, null, Perfil.PacientesId);
        var otherUser = CreateUser("Outro Paciente", "outro@hemodinks.com", "11144477735", DateTime.UtcNow, null, Perfil.PacientesId);
        context.Pacientes.AddRange(
            new Paciente { User = currentUser, NomePaciente = currentUser.Nome },
            new Paciente { User = otherUser, NomePaciente = otherUser.Nome });
        await context.SaveChangesAsync();

        var handler = new GetAllPacientesQueryHandler(context, NullLogger<GetAllPacientesQueryHandler>.Instance);
        var result = await handler.Handle(new GetAllPacientesQuery
        {
            Page = 1,
            PageSize = 10,
            CurrentPerfilId = Perfil.PacientesId,
            CurrentUserId = currentUser.Id
        }, CancellationToken.None);

        var ownRecord = Assert.Single(result.Items);
        Assert.Equal(currentUser.Id, ownRecord.UserId);
        Assert.Equal("Paciente Atual", ownRecord.NomePaciente);
    }

    [Fact]
    public async Task GetAllPacientes_WhenLoggedDoctor_ReturnsAuxiliaryAndGroupPatients()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = CreateUser("Dr. George", "dr.george.grupo@hemodinks.com", "39053344705", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null);
        var groupDoctor = CreateUser("Dra. Grupo", "dra.grupo@hemodinks.com", "11144477735", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null);
        var outsideDoctor = CreateUser("Dr. Fora", "dr.fora@hemodinks.com", "93541134780", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null);
        var titular = CreateUser("Paciente Titular", "paciente.titular@hemodinks.com", "52998224725", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);
        var auxiliar = CreateUser("Paciente Auxiliar", "paciente.auxiliar@hemodinks.com", "98765432100", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);
        var grupo = CreateUser("Paciente Grupo", "paciente.grupo@hemodinks.com", "12345678909", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);
        var fora = CreateUser("Paciente Fora", "paciente.fora@hemodinks.com", "01234567890", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), null, Perfil.PacientesId);

        context.GruposMedicos.Add(new GrupoMedico
        {
            Nome = "Grupo compartilhado",
            Membros =
            [
                new GrupoMedicoUsuario { User = doctor },
                new GrupoMedicoUsuario { User = groupDoctor }
            ]
        });

        context.Pacientes.AddRange(
            new Paciente
            {
                User = titular,
                NomePaciente = titular.Nome,
                MedicoUser = doctor,
                Medico = doctor.Nome,
            },
            new Paciente
            {
                User = auxiliar,
                NomePaciente = auxiliar.Nome,
                MedicoUser = outsideDoctor,
                Medico = outsideDoctor.Nome,
                MedicoAuxiliar1User = doctor,
                MedicoAuxiliar1 = doctor.Nome,
            },
            new Paciente
            {
                User = grupo,
                NomePaciente = grupo.Nome,
                MedicoUser = groupDoctor,
                Medico = groupDoctor.Nome,
            },
            new Paciente
            {
                User = fora,
                NomePaciente = fora.Nome,
                MedicoUser = outsideDoctor,
                Medico = outsideDoctor.Nome,
            });
        await context.SaveChangesAsync();

        var handler = new GetAllPacientesQueryHandler(context, NullLogger<GetAllPacientesQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllPacientesQuery
        {
            Page = 1,
            PageSize = 10,
            SortBy = "nome",
            SortDirection = "asc",
            CurrentPerfilId = Perfil.MedicosId,
            CurrentUserId = doctor.Id
        }, CancellationToken.None);

        Assert.Equal(["Paciente Auxiliar", "Paciente Grupo", "Paciente Titular"], result.Items.Select(paciente => paciente.NomePaciente));
        Assert.Equal(3, result.TotalItems);
    }

    private static User CreateUser(
        string nome,
        string email,
        string cpf,
        DateTime dataCadastro,
        DateTime? dataAtualizacao,
        int perfilId = Perfil.MedicosId)
    {
        return new User
        {
            Nome = nome,
            Email = email,
            Telefone = "+5511999999999",
            Cpf = cpf,
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataCadastro = dataCadastro,
            DataAtualizacao = dataAtualizacao,
            DataNascimento = new DateTime(1990, 1, 1),
            Ativo = true,
            PrecisaTrocarSenha = true,
            PerfilId = perfilId
        };
    }
}
