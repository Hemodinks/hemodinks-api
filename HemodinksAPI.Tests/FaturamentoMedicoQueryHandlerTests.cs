using HemodinksAPI.Application.Features.Faturamentos.Queries;
using HemodinksAPI.Application.Features.Faturamentos;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class FaturamentoMedicoQueryHandlerTests
{
    [Fact]
    public async Task GetAllFaturamentosMedicos_WhenDoctor_ReturnsOnlyPrimaryDoctorBillings()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = CreateDoctor("Dra. Ana", "dra.ana.faturamento@hemodinks.com", "52998224725");
        var otherDoctor = CreateDoctor("Dr. Bruno", "dr.bruno.faturamento@hemodinks.com", "11144477735");
        var patientUser = CreatePatientUser("Paciente Principal", "principal@hemodinks.com", "93541134780");
        var assistantUser = CreatePatientUser("Paciente Auxiliar", "auxiliar@hemodinks.com", "76009277672");
        var otherUser = CreatePatientUser("Paciente Outro", "outro@hemodinks.com", "39053344705");

        context.Pacientes.AddRange(
            new Paciente
            {
                User = patientUser,
                NomePaciente = patientUser.Nome,
                MedicoUser = doctor,
                Medico = doctor.Nome,
                Pagamento = "R$ 1.000,00",
                FaturamentoMedico = new FaturamentoMedico { HonorariosCirurgiao = 1000m }
            },
            new Paciente
            {
                User = assistantUser,
                NomePaciente = assistantUser.Nome,
                MedicoUser = otherDoctor,
                Medico = otherDoctor.Nome,
                MedicoAuxiliar1User = doctor,
                MedicoAuxiliar1 = doctor.Nome,
                Pagamento = "R$ 2.000,00",
                FaturamentoMedico = new FaturamentoMedico { HonorariosCirurgiao = 2000m }
            },
            new Paciente
            {
                User = otherUser,
                NomePaciente = otherUser.Nome,
                MedicoUser = otherDoctor,
                Medico = otherDoctor.Nome,
                Pagamento = "R$ 3.000,00",
                FaturamentoMedico = new FaturamentoMedico { HonorariosCirurgiao = 3000m }
            });
        await context.SaveChangesAsync();

        var handler = new GetAllFaturamentosMedicosQueryHandler(
            context,
            NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllFaturamentosMedicosQuery
        {
            CurrentPerfilId = Perfil.MedicosId,
            CurrentUserId = doctor.Id,
            Page = 1,
            PageSize = 10
        }, CancellationToken.None);

        Assert.Equal(["Paciente Principal"], result.Items.Select(item => item.NomePaciente));
        Assert.Equal(1, result.TotalItems);
    }

    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.ControllerId)]
    public async Task GetAllFaturamentosMedicos_WhenAdminOrController_ReturnsAllBillings(int perfilId)
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = CreateDoctor("Dra. Ana", $"dra.ana.{perfilId}@hemodinks.com", "52998224725");
        var otherDoctor = CreateDoctor("Dr. Bruno", $"dr.bruno.{perfilId}@hemodinks.com", "11144477735");

        context.Pacientes.AddRange(
            new Paciente
            {
                User = CreatePatientUser("Paciente A", $"paciente.a.{perfilId}@hemodinks.com", "93541134780"),
                NomePaciente = "Paciente A",
                MedicoUser = doctor,
                Medico = doctor.Nome,
                FaturamentoMedico = new FaturamentoMedico { HonorariosCirurgiao = 100m }
            },
            new Paciente
            {
                User = CreatePatientUser("Paciente B", $"paciente.b.{perfilId}@hemodinks.com", "76009277672"),
                NomePaciente = "Paciente B",
                MedicoUser = otherDoctor,
                Medico = otherDoctor.Nome,
                FaturamentoMedico = new FaturamentoMedico { HonorariosCirurgiao = 200m }
            });
        await context.SaveChangesAsync();

        var handler = new GetAllFaturamentosMedicosQueryHandler(
            context,
            NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllFaturamentosMedicosQuery
        {
            CurrentPerfilId = perfilId,
            CurrentUserId = 999,
            Page = 1,
            PageSize = 10
        }, CancellationToken.None);

        Assert.Equal(["Paciente A", "Paciente B"], result.Items.Select(item => item.NomePaciente).Order());
        Assert.Equal(2, result.TotalItems);
    }

    [Fact]
    public async Task GetAllFaturamentosMedicos_WithoutCompetencia_ReturnsLegacyPatientsWithoutBillingDate()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = CreateDoctor("Dra. Sem Data", "sem.data.faturamento@hemodinks.com", "39672548001");

        context.Pacientes.Add(new Paciente
        {
            User = CreatePatientUser("Paciente Sem Data", "paciente.sem.data@hemodinks.com", "84804257043"),
            NomePaciente = "Paciente Sem Data",
            Data = null,
            MedicoUser = doctor,
            Medico = doctor.Nome,
            Pagamento = "R$ 1.500,00"
        });
        await context.SaveChangesAsync();

        var handler = new GetAllFaturamentosMedicosQueryHandler(
            context,
            NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllFaturamentosMedicosQuery
        {
            CurrentPerfilId = Perfil.AdministradorId,
            CurrentUserId = 999,
            Page = 1,
            PageSize = 10
        }, CancellationToken.None);

        Assert.Equal(["Paciente Sem Data"], result.Items.Select(item => item.NomePaciente));
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task GetAllFaturamentosMedicos_WithCompetenciaRange_ReturnsBillingsCreatedInSelectedMonth()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = CreateDoctor("Dra. Competencia", "competencia.faturamento@hemodinks.com", "10052597001");

        context.Pacientes.AddRange(
            new Paciente
            {
                User = CreatePatientUser("Paciente Junho", "junho.competencia@hemodinks.com", "98606165059"),
                NomePaciente = "Paciente Junho",
                Data = new DateTime(2026, 6, 20),
                MedicoUser = doctor,
                Medico = doctor.Nome,
                FaturamentoMedico = new FaturamentoMedico
                {
                    HonorariosCirurgiao = 100m,
                    DataCadastro = new DateTime(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc),
                    CompetenciaInicio = new DateTime(2026, 7, 1),
                    CompetenciaFinal = new DateTime(2026, 7, 31)
                }
            },
            new Paciente
            {
                User = CreatePatientUser("Paciente Julho", "julho.competencia@hemodinks.com", "95880630058"),
                NomePaciente = "Paciente Julho",
                Data = new DateTime(2026, 6, 15),
                MedicoUser = doctor,
                Medico = doctor.Nome,
                FaturamentoMedico = new FaturamentoMedico
                {
                    HonorariosCirurgiao = 200m,
                    DataCadastro = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc),
                    CompetenciaInicio = new DateTime(2026, 6, 1),
                    CompetenciaFinal = new DateTime(2026, 6, 30)
                }
            },
            new Paciente
            {
                User = CreatePatientUser("Paciente Legado", "legado.competencia@hemodinks.com", "25235576091"),
                NomePaciente = "Paciente Legado",
                Data = new DateTime(2026, 9, 25),
                MedicoUser = doctor,
                Medico = doctor.Nome,
                FaturamentoMedico = new FaturamentoMedico
                {
                    HonorariosCirurgiao = 300m,
                    DataCadastro = new DateTime(2026, 7, 25, 10, 0, 0, DateTimeKind.Utc)
                }
            },
            new Paciente
            {
                User = CreatePatientUser("Paciente Agosto", "agosto.competencia@hemodinks.com", "43181551005"),
                NomePaciente = "Paciente Agosto",
                Data = new DateTime(2026, 8, 1),
                MedicoUser = doctor,
                Medico = doctor.Nome,
                FaturamentoMedico = new FaturamentoMedico
                {
                    HonorariosCirurgiao = 400m,
                    DataCadastro = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc),
                    CompetenciaInicio = new DateTime(2026, 7, 1),
                    CompetenciaFinal = new DateTime(2026, 7, 31)
                }
            });
        await context.SaveChangesAsync();

        var handler = new GetAllFaturamentosMedicosQueryHandler(
            context,
            NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllFaturamentosMedicosQuery
        {
            CurrentPerfilId = Perfil.AdministradorId,
            CurrentUserId = 999,
            Page = 1,
            PageSize = 10,
            CompetenciaInicio = new DateTime(2026, 7, 1),
            CompetenciaFinal = new DateTime(2026, 7, 1)
        }, CancellationToken.None);

        Assert.Equal(["Paciente Julho", "Paciente Legado"], result.Items.Select(item => item.NomePaciente).Order());
        Assert.Equal(2, result.TotalItems);
    }

    [Fact]
    public async Task GetAllFaturamentosMedicos_WithCompetenciaRange_IncludesLegacyPatientsByDisplayedCadastroDate()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = CreateDoctor("Dra. Legado", "legado.sem.faturamento@hemodinks.com", "82620466016");
        var insideUser = CreatePatientUser("Paciente Dentro", "dentro.sem.faturamento@hemodinks.com", "72863128006");
        var outsideUser = CreatePatientUser("Paciente Fora", "fora.sem.faturamento@hemodinks.com", "96980480017");

        insideUser.DataCadastro = new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc);
        outsideUser.DataCadastro = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);

        context.Pacientes.AddRange(
            new Paciente
            {
                User = insideUser,
                NomePaciente = "Paciente Dentro",
                Data = null,
                MedicoUser = doctor,
                Medico = doctor.Nome,
                Pagamento = "R$ 1.500,00"
            },
            new Paciente
            {
                User = outsideUser,
                NomePaciente = "Paciente Fora",
                Data = null,
                MedicoUser = doctor,
                Medico = doctor.Nome,
                Pagamento = "R$ 1.500,00"
            });
        await context.SaveChangesAsync();

        var handler = new GetAllFaturamentosMedicosQueryHandler(
            context,
            NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance);

        var result = await handler.Handle(new GetAllFaturamentosMedicosQuery
        {
            CurrentPerfilId = Perfil.AdministradorId,
            CurrentUserId = 999,
            Page = 1,
            PageSize = 10,
            CompetenciaInicio = new DateTime(2026, 7, 1),
            CompetenciaFinal = new DateTime(2026, 7, 1)
        }, CancellationToken.None);

        Assert.Equal(["Paciente Dentro"], result.Items.Select(item => item.NomePaciente));
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public void EnsureSynced_WhenPacienteHasData_SetsMonthlyCompetencia()
    {
        var paciente = new Paciente
        {
            Data = new DateTime(2026, 7, 15),
            NomePaciente = "Paciente Competencia",
            Procedimentos = []
        };

        var faturamento = FaturamentoMedicoSync.EnsureSynced(paciente, new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 7, 1), faturamento.CompetenciaInicio);
        Assert.Equal(new DateTime(2026, 7, 31), faturamento.CompetenciaFinal);
    }

    private static User CreateDoctor(string nome, string email, string cpf)
    {
        return new User
        {
            Nome = nome,
            Email = email,
            Telefone = "+5581999887766",
            Cpf = cpf,
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
    }

    private static User CreatePatientUser(string nome, string email, string cpf)
    {
        return new User
        {
            Nome = nome,
            Email = email,
            Telefone = "+5581999999999",
            Cpf = cpf,
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
    }
}
