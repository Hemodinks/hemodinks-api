using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class PacienteCommandHandlerTests
{
    [Fact]
    public async Task UpdatePaciente_WhenLoggedUserIsPatient_ThrowsUnauthorizedAccessException()
    {
        await using var context = TestDbContextFactory.Create();
        var user = new User
        {
            Nome = "Paciente Bloqueado",
            Email = "paciente.bloqueado@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome,
            Medico = "Dra. Ana"
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new UpdatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new FakeProfilePhotoStorage(),
            NullLogger<UpdatePacienteCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new UpdatePacienteCommand
        {
            Id = paciente.Id,
            NomePaciente = "Paciente Editado",
            Email = user.Email,
            Telefone = user.Telefone,
            Cpf = user.Cpf!,
            DataNascimento = user.DataNascimento!.Value,
            Ativo = true,
            CurrentUserId = user.Id,
            CurrentPerfilId = Perfil.PacientesId,
            CurrentUserName = user.Nome
        }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdatePaciente_WhenAdministrator_UpdatesPaciente()
    {
        await using var context = TestDbContextFactory.Create();
        var doctorName = "Dra. Ana";
        var doctor = new User
        {
            Nome = doctorName,
            Email = "dra.ana@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "52998224725",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        var user = new User
        {
            Nome = "Paciente Relacionado",
            Email = "paciente.relacionado@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "11144477735",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome,
            MedicoUser = doctor,
            Medico = doctorName
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new UpdatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new FakeProfilePhotoStorage(),
            NullLogger<UpdatePacienteCommandHandler>.Instance);

        var response = await handler.Handle(new UpdatePacienteCommand
        {
            Id = paciente.Id,
            NomePaciente = "Paciente Atualizado",
            Diagnostico = "Diagnostico atualizado",
            TratamentoMedico = "Tratamento atualizado",
            Email = user.Email,
            Telefone = user.Telefone,
            Cpf = user.Cpf!,
            DataNascimento = user.DataNascimento!.Value,
            Ativo = true,
            HospitalId = 2,
            MedicoUserId = doctor.Id,
            Medico = doctorName,
            OpmeFornecedorId = 3,
            OpmeFornecedor = "GE",
            Pagamento = "R$ 2.500,00",
            RepasseGlosa = "R$ 125,50",
            StatusPago = true,
            CurrentUserId = 99,
            CurrentPerfilId = Perfil.AdministradorId,
            CurrentUserName = "Admin"
        }, CancellationToken.None);

        Assert.Equal("Paciente Atualizado", response.NomePaciente);
        Assert.Equal("Diagnostico atualizado", response.Diagnostico);
        Assert.Equal("Tratamento atualizado", response.TratamentoMedico);
        Assert.Equal(2, response.HospitalId);
        Assert.Equal("Santa Genoveva - Mater Dei", response.Hospital);
        Assert.Equal(doctorName, response.Medico);
        Assert.Equal(3, response.OpmeFornecedorId);
        Assert.Equal("GE", response.OpmeFornecedor);
        var storedFaturamento = await context.FaturamentosMedicos.SingleAsync();
        Assert.Equal(2500m, storedFaturamento.HonorariosCirurgiao);
        Assert.Equal(125.50m, storedFaturamento.ValorGlosa);
        Assert.Equal(2374.50m, storedFaturamento.RepasseMedico);
        Assert.Equal("GE", storedFaturamento.OpmeMateriaisEspeciais);
        Assert.True(storedFaturamento.ConferenciaPagamentoRealizada);
        var storedUser = await context.Users.SingleAsync(storedUser => storedUser.Id == user.Id);
        Assert.NotNull(storedUser.DataAtualizacao);
        Assert.Equal(storedUser.DataAtualizacao, response.DataAtualizacao);
    }

    [Fact]
    public async Task UpdatePaciente_WhenController_UpdatesPaciente()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.controller@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "52998224725",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        var user = new User
        {
            Nome = "Paciente Controller",
            Email = "paciente.controller@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "11144477735",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome,
            MedicoUser = doctor,
            Medico = doctor.Nome
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new UpdatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new FakeProfilePhotoStorage(),
            NullLogger<UpdatePacienteCommandHandler>.Instance);

        var response = await handler.Handle(new UpdatePacienteCommand
        {
            Id = paciente.Id,
            NomePaciente = "Paciente Controller Atualizado",
            Email = user.Email,
            Telefone = user.Telefone,
            Cpf = user.Cpf!,
            DataNascimento = user.DataNascimento!.Value,
            Ativo = true,
            HospitalId = 2,
            CurrentUserId = 999,
            CurrentPerfilId = Perfil.ControllerId,
            CurrentUserName = "Controller"
        }, CancellationToken.None);

        Assert.Equal("Paciente Controller Atualizado", response.NomePaciente);
    }

    [Fact]
    public async Task UpdatePaciente_WhenLoggedDoctorIsRelated_UpdatesPaciente()
    {
        await using var context = TestDbContextFactory.Create();
        var doctorName = "Dra. Ana";
        var doctor = new User
        {
            Nome = doctorName,
            Email = "dra.ana.relacionada@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "52998224725",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        var user = new User
        {
            Nome = "Paciente Relacionado",
            Email = "paciente.relacionado.medico@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "11144477735",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome,
            HospitalId = 2,
            Hospital = "Hospital Relacionado",
            MedicoUser = doctor,
            Medico = doctorName
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new UpdatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new FakeProfilePhotoStorage(),
            NullLogger<UpdatePacienteCommandHandler>.Instance);

        var response = await handler.Handle(new UpdatePacienteCommand
        {
            Id = paciente.Id,
            NomePaciente = "Paciente Atualizado",
            Email = user.Email,
            Telefone = user.Telefone,
            Cpf = user.Cpf!,
            DataNascimento = user.DataNascimento!.Value,
            Ativo = true,
            HospitalId = 2,
            CurrentUserId = doctor.Id,
            CurrentPerfilId = Perfil.MedicosId,
            CurrentUserName = doctorName
        }, CancellationToken.None);

        Assert.Equal("Paciente Atualizado", response.NomePaciente);
        Assert.Equal(2, response.HospitalId);
    }

}
