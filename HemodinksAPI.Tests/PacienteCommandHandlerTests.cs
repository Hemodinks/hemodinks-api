using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class PacienteCommandHandlerTests
{
    [Fact]
    public async Task CreatePaciente_CreatesLinkedUserWithPacienteProfile()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        var auxiliar1 = new User
        {
            Nome = "Dr. Bruno",
            Email = "dr.bruno@hemodinks.com",
            Telefone = "+5581999887767",
            Cpf = "76109277673",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1986, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        var auxiliar2 = new User
        {
            Nome = "Dra. Clara",
            Email = "dra.clara@hemodinks.com",
            Telefone = "+5581999887768",
            Cpf = "76009277672",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1987, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        context.CbhpmGeral.Add(new CbhpmGeral
        {
            Codigo = "1.01.01.01-2",
            Procedimento = "Em consultorio",
            Porte = "2B",
            ValorReferencia = 120m
        });
        context.CbhpmGeral.Add(new CbhpmGeral
        {
            Codigo = "1.01.02.01-9",
            Procedimento = "Visita hospitalar a paciente internado",
            Porte = "2A",
            ValorReferencia = 180m
        });
        context.Users.AddRange(doctor, auxiliar1, auxiliar2);
        await context.SaveChangesAsync();

        var hasher = new PasswordHasher();
        var invitationSender = new RecordingPasswordResetNotificationSender();
        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            hasher,
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance,
            invitationSender);

        var response = await handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente Novo",
            Diagnostico = "Diagnostico clinico de teste",
            TratamentoMedico = "Tratamento clinico de teste",
            Email = "paciente.novo@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "52998224725",
            DataNascimento = new DateTime(1990, 1, 1),
            Data = new DateTime(2026, 6, 1),
            HospitalId = 1,
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome,
            MedicoAuxiliar1UserId = auxiliar1.Id,
            MedicoAuxiliar1 = auxiliar1.Nome,
            MedicoAuxiliar2UserId = auxiliar2.Id,
            MedicoAuxiliar2 = auxiliar2.Nome,
            ConvenioId = 7,
            Convenio = "Particular",
            OpmeFornecedor = "Fornecedor Manual OPME",
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto { CbhpmCodigo = "10101012" },
                new PacienteProcedimentoCommandDto { CbhpmCodigo = "1.01.02.01-9" }
            ],
            Autorizacao = "AUT-123",
            Pagamento = "Pix",
            RepasseGlosa = "Sem glosa",
            StatusPago = true,
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync(user => user.PerfilId == Perfil.PacientesId);
        var storedPaciente = await context.Pacientes.SingleAsync();

        Assert.Equal(storedUser.Id, storedPaciente.UserId);
        Assert.Equal(Perfil.PacientesId, storedUser.PerfilId);
        Assert.Equal("Paciente Novo", storedUser.Nome);
        Assert.Equal("Diagnostico clinico de teste", storedPaciente.Diagnostico);
        Assert.Equal("Tratamento clinico de teste", storedPaciente.TratamentoMedico);
        Assert.Equal("52998224725", storedUser.Cpf);
        Assert.True(response.ConvitePrimeiroAcessoEnviado);
        Assert.Single(invitationSender.Notifications);
        Assert.Equal(storedUser.Email, invitationSender.Notifications[0].Email);
        Assert.NotEmpty(await context.PasswordResetTokens.ToListAsync());
        Assert.False(hasher.VerifyPassword(DefaultUserPassword.Value, storedUser.Senha));
        Assert.Equal(1, storedPaciente.HospitalId);
        Assert.Equal("Santa Clara - Mater Dei", storedPaciente.Hospital);
        Assert.Equal(doctor.Id, storedPaciente.MedicoUserId);
        Assert.Equal(doctor.Nome, storedPaciente.Medico);
        Assert.Equal(auxiliar1.Id, storedPaciente.MedicoAuxiliar1UserId);
        Assert.Equal(auxiliar1.Nome, storedPaciente.MedicoAuxiliar1);
        Assert.Equal(auxiliar2.Id, storedPaciente.MedicoAuxiliar2UserId);
        Assert.Equal(auxiliar2.Nome, storedPaciente.MedicoAuxiliar2);
        Assert.Equal(7, storedPaciente.ConvenioId);
        Assert.Equal("Particular", storedPaciente.Convenio);
        Assert.Equal("Fornecedor Manual OPME", storedPaciente.OpmeFornecedor);
        Assert.Equal("10101012", storedPaciente.CbhpmCodigo);
        Assert.Equal("Em consultorio", storedPaciente.Procedimento);
        Assert.Equal("2B", storedPaciente.CbhpmPorte);
        Assert.True(storedPaciente.StatusPago);
        Assert.Empty(await context.FaturamentosMedicos.ToListAsync());
        Assert.Equal(storedPaciente.Id, response.Id);
        Assert.Equal("Diagnostico clinico de teste", response.Diagnostico);
        Assert.Equal("Tratamento clinico de teste", response.TratamentoMedico);
        Assert.Equal(storedUser.Id, response.UserId);
        Assert.Equal(7, response.ConvenioId);
        Assert.Equal("Particular", response.Convenio);
        Assert.Equal("Fornecedor Manual OPME", response.OpmeFornecedor);
        Assert.Contains(await context.OPME.ToListAsync(), item => item.Fornecedor == "Fornecedor Manual OPME");
        Assert.Equal(auxiliar1.Id, response.MedicoAuxiliar1UserId);
        Assert.Equal(auxiliar1.Nome, response.MedicoAuxiliar1);
        Assert.Equal(auxiliar2.Id, response.MedicoAuxiliar2UserId);
        Assert.Equal(auxiliar2.Nome, response.MedicoAuxiliar2);
        Assert.Equal(["Em consultorio", "Visita hospitalar a paciente internado"], response.Procedimentos.Select(item => item.Procedimento));

        var storedProcedimentos = await context.PacienteProcedimentos
            .OrderBy(item => item.Ordem)
            .ToListAsync();
        Assert.Equal(2, storedProcedimentos.Count);
        Assert.Equal(storedPaciente.Id, storedProcedimentos[0].PacienteId);
        Assert.Equal("10101012", storedProcedimentos[0].CbhpmCodigo);
        Assert.Equal(120m, storedProcedimentos[0].ValorReferencia);
        Assert.Equal("10102019", storedProcedimentos[1].CbhpmCodigo);
        Assert.Equal(180m, storedProcedimentos[1].ValorReferencia);
    }

    [Fact]
    public async Task CreatePaciente_WhenHospitalAndConvenioAreManual_PersistsLookupsAndProcedimentos()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana Manual",
            Email = "dra.ana.manual.lookup@hemodinks.com",
            Telefone = "+5581999887711",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        context.Users.Add(doctor);
        context.CbhpmGeral.Add(new CbhpmGeral
        {
            Codigo = "1.01.01.01-2",
            Procedimento = "Em consultorio",
            Porte = "2B",
            ValorReferencia = 120m
        });
        await context.SaveChangesAsync();

        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);

        var response = await handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente Manual Completo",
            Email = "paciente.manual.completo@hemodinks.com",
            Telefone = "+5581999999911",
            Cpf = "52998224725",
            DataNascimento = new DateTime(1990, 1, 1),
            Data = new DateTime(2026, 6, 23),
            Hospital = "Hospital Manual Santa Joana",
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome,
            Convenio = "Convenio Manual Porto Seguro",
            OpmeFornecedor = "Fornecedor Manual Gtech",
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto { CbhpmCodigo = "10101012" },
                new PacienteProcedimentoCommandDto
                {
                    CbhpmCodigo = "25252525",
                    CbhpmPorte = "25B",
                    Procedimento = "George Procedimento",
                    ValorReferencia = 2500m
                }
            ],
            Autorizacao = "Foi ok",
            Pagamento = "R$ 2.500,00",
            RepasseGlosa = "R$ 100,00",
            StatusPago = true,
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        var storedPaciente = await context.Pacientes
            .Include(paciente => paciente.HospitalReferencia)
            .Include(paciente => paciente.ConvenioReferencia)
            .Include(paciente => paciente.OpmeFornecedorReferencia)
            .Include(paciente => paciente.Procedimentos)
            .SingleAsync();
        var storedProcedimentos = storedPaciente.Procedimentos.OrderBy(item => item.Ordem).ToList();
        var storedHospital = await context.Hospitais.SingleAsync(item => item.Nome == "Hospital Manual Santa Joana");
        var storedConvenio = await context.Convenios.SingleAsync(item => item.DescricaoConvenio == "Convenio Manual Porto Seguro");

        Assert.Equal(storedHospital.Id, storedPaciente.HospitalId);
        Assert.Equal("Hospital Manual Santa Joana", storedPaciente.Hospital);
        Assert.Equal(storedConvenio.IdConvenio, storedPaciente.ConvenioId);
        Assert.Equal("Convenio Manual Porto Seguro", storedPaciente.Convenio);
        Assert.Equal("Fornecedor Manual Gtech", storedPaciente.OpmeFornecedor);
        Assert.Equal("10101012", storedPaciente.CbhpmCodigo);
        Assert.Equal("Em consultorio", storedPaciente.Procedimento);
        Assert.Equal(2, storedProcedimentos.Count);
        Assert.Equal("10101012", storedProcedimentos[0].CbhpmCodigo);
        Assert.Equal("Em consultorio", storedProcedimentos[0].Procedimento);
        Assert.Equal("25252525", storedProcedimentos[1].CbhpmCodigo);
        Assert.Equal("George Procedimento", storedProcedimentos[1].Procedimento);
        Assert.Equal("25B", storedProcedimentos[1].CbhpmPorte);
        Assert.Equal(2500m, storedProcedimentos[1].ValorReferencia);
        Assert.Equal(storedHospital.Id, response.HospitalId);
        Assert.Equal("Hospital Manual Santa Joana", response.Hospital);
        Assert.Equal(storedConvenio.IdConvenio, response.ConvenioId);
        Assert.Equal("Convenio Manual Porto Seguro", response.Convenio);
        Assert.Equal(["Em consultorio", "George Procedimento"], response.Procedimentos.Select(item => item.Procedimento));
        Assert.Contains(await context.OPME.ToListAsync(), item => item.Fornecedor == "Fornecedor Manual Gtech");
    }

}
