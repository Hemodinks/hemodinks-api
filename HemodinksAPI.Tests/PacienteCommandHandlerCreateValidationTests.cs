using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class PacienteCommandHandlerTests
{
    [Fact]
    public async Task CreatePaciente_WithProcedureInNonDefaultClinic_UsesCurrentClinicForProcedure()
    {
        const int clinicId = 7;
        var clinicContext = new ClinicaContext();
        clinicContext.SetCurrent(clinicId, "clinica-sete");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new AppDbContext(options, clinicContext);
        context.Database.EnsureCreated();
        context.Clinicas.Add(new Clinica
        {
            Id = clinicId,
            Nome = "Clinica Sete",
            Slug = "clinica-sete"
        });
        await context.SaveChangesAsync();

        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            clinicContext,
            NullLogger<CreatePacienteCommandHandler>.Instance);

        await handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente com procedimento cirurgico",
            Hospital = "Hospital Manual da Clinica Sete",
            Convenio = "Convenio Manual da Clinica Sete",
            OpmeFornecedor = "OPME Manual da Clinica Sete",
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto
                {
                    Procedimento = "Procedimento cirurgico manual",
                    CbhpmPorte = "2A"
                }
            ],
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        var procedure = await context.PacienteProcedimentos.SingleAsync();
        Assert.Equal(clinicId, procedure.ClinicaId);
        Assert.Equal(clinicId, (await context.Hospitais.SingleAsync()).ClinicaId);
        Assert.Equal(clinicId, (await context.Convenios.SingleAsync()).ClinicaId);
        Assert.Equal(clinicId, (await context.OPME.SingleAsync()).ClinicaId);
    }

    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.SuperAdministradorId)]
    public async Task CreatePaciente_WhenAdministratorSelectsTeamMember_AcceptsMemberAsSurgeon(int perfilId)
    {
        await using var context = TestDbContextFactory.Create();
        var teamMember = new User
        {
            Nome = "Pessoa da Equipe",
            Email = $"pessoa.equipe-{perfilId}@hemodinks.com",
            Telefone = "+5581999887700",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
            PerfilId = Perfil.EquipeId
        };
        context.Equipes.Add(new Equipe
        {
            ClinicaId = Clinica.DefaultId,
            Nome = "Equipe Cirurgica",
            UsuarioLogin = new User
            {
                Nome = "Login da Equipe",
                Email = $"login.equipe-{perfilId}@hemodinks.com",
                Telefone = "+5581999887701",
                Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
                PerfilId = Perfil.EquipeId
            },
            Membros =
            [
                new EquipeMembro { ClinicaId = Clinica.DefaultId, User = teamMember }
            ]
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
            NomePaciente = "Paciente da Equipe",
            HospitalId = 1,
            MedicoUserId = teamMember.Id,
            CurrentPerfilId = perfilId
        }, CancellationToken.None);

        Assert.Equal(teamMember.Id, response.MedicoUserId);
        Assert.Equal(teamMember.Nome, response.Medico);
    }

    [Fact]
    public async Task CreatePaciente_WhenControllerProfile_CreatesPaciente()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Controller",
            Email = "dra.controller@hemodinks.com",
            Telefone = "+5581999887761",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        context.CbhpmGeral.Add(new CbhpmGeral
        {
            Codigo = "1.01.01.01-2",
            Procedimento = "Em consultorio",
            Porte = "2B",
            ValorReferencia = 120m
        });
        context.Users.Add(doctor);
        await context.SaveChangesAsync();

        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);

        var response = await handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente Controller",
            DataNascimento = new DateTime(1990, 1, 1),
            Data = new DateTime(2026, 6, 10),
            HospitalId = 1,
            MedicoUserId = doctor.Id,
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto { CbhpmCodigo = "10101012" }
            ],
            CurrentPerfilId = Perfil.ControllerId,
            CurrentUserId = 999,
            CurrentUserName = "Controller Teste"
        }, CancellationToken.None);

        var storedPaciente = await context.Pacientes.SingleAsync();
        Assert.Equal("Paciente Controller", storedPaciente.NomePaciente);
        Assert.Equal(doctor.Id, storedPaciente.MedicoUserId);
        Assert.Equal(response.Id, storedPaciente.Id);
    }

    [Fact]
    public async Task CreatePaciente_WithoutCpfEmailTelefoneAndBirth_AcceptsOptionalData()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.sem.contato@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
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
            NomePaciente = "Paciente Sem Contato",
            HospitalId = 1,
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome,
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto { CbhpmCodigo = "10101012" }
            ],
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync(user => user.PerfilId == Perfil.PacientesId);

        Assert.Null(storedUser.Cpf);
        Assert.Null(storedUser.DataNascimento);
        Assert.Empty(storedUser.Telefone);
        Assert.StartsWith("paciente-", storedUser.Email);
        Assert.EndsWith("@hemodinks.local", storedUser.Email);
        Assert.Equal(storedUser.Id, response.UserId);
    }

    [Fact]
    public async Task CreatePaciente_WhenMedicalTeamHasDuplicates_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.duplicada@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        context.Users.Add(doctor);
        await context.SaveChangesAsync();

        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente Duplicado",
            DataNascimento = new DateTime(1990, 1, 1),
            HospitalId = 1,
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome,
            MedicoAuxiliar1UserId = doctor.Id,
            MedicoAuxiliar1 = doctor.Nome,
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto
                {
                    Procedimento = "Procedimento manual Hemodinks",
                    CbhpmPorte = "1A"
                }
            ],
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None));

        Assert.Equal("Cirurgiao e medicos auxiliares devem ser diferentes", exception.Message);
        Assert.Empty(await context.Pacientes.ToListAsync());
    }

    [Fact]
    public async Task CreatePaciente_WhenCbhpmCodeDoesNotExist_StoresManualProcedure()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.manual@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        context.Users.Add(doctor);
        await context.SaveChangesAsync();

        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);

        var response = await handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente Manual",
            Email = "paciente.manual@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "52998224725",
            DataNascimento = new DateTime(1990, 1, 1),
            Data = new DateTime(2026, 6, 1),
            HospitalId = 1,
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome,
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto
                {
                    CbhpmCodigo = "9.99.99.99-9",
                    Procedimento = "Procedimento manual Hemodinks",
                    CbhpmPorte = "1A",
                    ValorReferencia = 250m
                }
            ],
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        var storedPaciente = await context.Pacientes
            .Include(paciente => paciente.Procedimentos)
            .SingleAsync();
        var storedProcedimento = Assert.Single(storedPaciente.Procedimentos);

        Assert.Equal("99999999", storedPaciente.CbhpmCodigo);
        Assert.Equal("Procedimento manual Hemodinks", storedPaciente.Procedimento);
        Assert.Equal("1A", storedPaciente.CbhpmPorte);
        Assert.Equal("99999999", response.CbhpmCodigo);
        Assert.Equal("Procedimento manual Hemodinks", response.Procedimento);
        Assert.Equal("99999999", storedProcedimento.CbhpmCodigo);
        Assert.Equal("Procedimento manual Hemodinks", storedProcedimento.Procedimento);
        Assert.Equal("1A", storedProcedimento.CbhpmPorte);
        Assert.Equal(250m, storedProcedimento.ValorReferencia);
        Assert.Empty(await context.CbhpmGeral.Where(item => item.Codigo == "9.99.99.99-9").ToListAsync());
    }

    [Fact]
    public async Task CreatePaciente_WhenCbhpmCodeDoesNotExistWithoutDescription_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.sem.descricao@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        context.Users.Add(doctor);
        await context.SaveChangesAsync();

        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente Manual",
            Email = "paciente.manual.sem.descricao@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "52998224725",
            DataNascimento = new DateTime(1990, 1, 1),
            Data = new DateTime(2026, 6, 1),
            HospitalId = 1,
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome,
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto { CbhpmCodigo = "9.99.99.99-9" }
            ],
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None));

        Assert.Equal("Informe a descricao do procedimento para o codigo CBHPM nao cadastrado", exception.Message);
        Assert.Empty(await context.Pacientes.ToListAsync());
        Assert.Empty(await context.Users.Where(user => user.PerfilId == Perfil.PacientesId).ToListAsync());
    }

    [Fact]
    public async Task CreatePaciente_WhenLoggedDoctor_CreatesPaciente()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.permitida@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
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
            NomePaciente = "Paciente Medico",
            DataNascimento = new DateTime(1990, 1, 1),
            Data = new DateTime(2026, 6, 10),
            HospitalId = 1,
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto { CbhpmCodigo = "10101012" }
            ],
            CurrentUserId = doctor.Id,
            CurrentUserName = doctor.Nome,
            CurrentPerfilId = Perfil.MedicosId,
        }, CancellationToken.None);

        var storedPaciente = await context.Pacientes.Include(paciente => paciente.User).SingleAsync();

        Assert.Equal("Paciente Medico", storedPaciente.NomePaciente);
        Assert.Equal(doctor.Id, storedPaciente.MedicoUserId);
        Assert.Equal(doctor.Nome, storedPaciente.Medico);
        Assert.Equal(response.Id, storedPaciente.Id);
        Assert.Equal(Perfil.PacientesId, storedPaciente.User.PerfilId);
    }

}
