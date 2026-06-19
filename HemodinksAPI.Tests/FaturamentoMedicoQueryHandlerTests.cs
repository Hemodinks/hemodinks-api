using HemodinksAPI.Application.Features.Faturamentos.Queries;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
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
