using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public partial class UserCommandHandlerTests
{
    [Fact]
    public async Task CreateUser_WhenEmailIsNew_CreatesActiveUserWithDefaultPassword()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var handler = new CreateUserCommandHandler(
            context,
            hasher,
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            Options.Create(new LicencaOptions()),
            NullLogger<CreateUserCommandHandler>.Instance);

        var response = await handler.Handle(new CreateUserCommand
        {
            Nome = "Novo Usuario",
            Email = "novo.usuario@email.com",
            Telefone = "+5511999999999",
            Cpf = "52998224725",
            Crm = "12345",
            CrmUf = "pe",
            FotoPerfil = "data:image/png;base64,avatar",
            DataNascimento = new DateTime(1990, 5, 15)
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        Assert.Equal(storedUser.Id, response.Id);
        Assert.Equal("Novo Usuario", storedUser.Nome);
        Assert.True(storedUser.Ativo);
        Assert.True(storedUser.PrecisaTrocarSenha);
        Assert.True(response.PrecisaTrocarSenha);
        Assert.Equal("https://storage.example/1.png", storedUser.FotoPerfil);
        Assert.Equal("https://storage.example/1.png", response.FotoPerfil);
        Assert.Equal(Perfil.MedicosId, storedUser.PerfilId);
        Assert.Equal(Perfil.MedicosId, response.PerfilId);
        Assert.Equal("12345", storedUser.Crm);
        Assert.Equal("PE", storedUser.CrmUf);
        Assert.Equal("12345", response.Crm);
        Assert.Equal("PE", response.CrmUf);
        Assert.Equal("Médicos", response.PerfilNome);
        Assert.True(hasher.VerifyPassword(DefaultUserPassword.Value, storedUser.Senha));
        Assert.NotNull(await context.Licencas.SingleOrDefaultAsync(item => item.UserId == storedUser.Id));
    }

    [Fact]
    public async Task CreateUser_WhenCpfAndBirthDateAreNotProvided_AllowsNullValues()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new CreateUserCommandHandler(
            context,
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            Options.Create(new LicencaOptions()),
            NullLogger<CreateUserCommandHandler>.Instance);

        var response = await handler.Handle(new CreateUserCommand
        {
            Nome = "Usuario Sem Cpf",
            Email = "sem.cpf@email.com",
            Telefone = "+5511999999999",
            Crm = "12345",
            CrmUf = "PE",
            PerfilId = Perfil.MedicosId
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        Assert.Null(storedUser.Cpf);
        Assert.Null(response.Cpf);
        Assert.Null(storedUser.DataNascimento);
        Assert.Null(response.DataNascimento);
    }

    [Fact]
    public async Task CreateUser_WhenMedicalProfileHasNoCrm_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new CreateUserCommandHandler(
            context,
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            Options.Create(new LicencaOptions()),
            NullLogger<CreateUserCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreateUserCommand
        {
            Nome = "Medico Sem Crm",
            Email = "medico.sem.crm@email.com",
            Telefone = "+5511999999999",
            Cpf = "93541134780",
            DataNascimento = new DateTime(1990, 5, 15),
            PerfilId = Perfil.MedicosId
        }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateUser_WhenControllerPerfilIsProvided_AssignsPerfil()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var handler = new CreateUserCommandHandler(
            context,
            hasher,
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            Options.Create(new LicencaOptions()),
            NullLogger<CreateUserCommandHandler>.Instance);

        var response = await handler.Handle(new CreateUserCommand
        {
            Nome = "Controller Teste",
            Email = "controller@email.com",
            Telefone = "+5511777777777",
            Cpf = "11144477735",
            DataNascimento = new DateTime(1992, 8, 10),
            PerfilId = Perfil.ControllerId
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        Assert.Empty(await context.Pacientes.ToListAsync());
        Assert.Equal(Perfil.ControllerId, storedUser.PerfilId);
        Assert.Equal(Perfil.ControllerId, response.PerfilId);
        Assert.Equal("Controller", response.PerfilNome);
    }

    [Fact]
    public async Task CreateUser_WhenPacientePerfilIsProvided_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new CreateUserCommandHandler(
            context,
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            Options.Create(new LicencaOptions()),
            NullLogger<CreateUserCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreateUserCommand
        {
            Nome = "Paciente Teste",
            Email = "paciente@email.com",
            Telefone = "+5511777777777",
            Cpf = "11144477735",
            DataNascimento = new DateTime(1992, 8, 10),
            PerfilId = Perfil.PacientesId
        }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateUser_WhenSuperAdministradorAssignsPacientePerfil_CreatesUser()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new CreateUserCommandHandler(
            context,
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            Options.Create(new LicencaOptions()),
            NullLogger<CreateUserCommandHandler>.Instance);

        var response = await handler.Handle(new CreateUserCommand
        {
            CurrentUser = new CurrentUserContext(
                99,
                Perfil.SuperAdministradorId,
                "Super Administrador"),
            Nome = "Paciente pelo Super",
            Email = "paciente.super@email.com",
            Telefone = "+5511777777777",
            Cpf = "11144477735",
            DataNascimento = new DateTime(1992, 8, 10),
            PerfilId = Perfil.PacientesId
        }, CancellationToken.None);

        Assert.Equal(Perfil.PacientesId, response.PerfilId);
        Assert.Equal(Perfil.PacientesId, (await context.Users.SingleAsync()).PerfilId);
        Assert.Single(await context.Pacientes.ToListAsync());
    }

    [Fact]
    public async Task CreateUser_WhenPerfilDoesNotExist_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new CreateUserCommandHandler(
            context,
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            Options.Create(new LicencaOptions()),
            NullLogger<CreateUserCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreateUserCommand
        {
            Nome = "Perfil Invalido",
            Email = "perfil.invalido@email.com",
            Telefone = "+5511666666666",
            Cpf = "93541134780",
            DataNascimento = new DateTime(1993, 4, 12),
            PerfilId = 999
        }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateUser_WhenEmailAlreadyExists_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        context.Users.Add(CreateUser(email: "duplicado@email.com", passwordHash: hasher.HashPassword("Senha@123")));
        await context.SaveChangesAsync();

        var handler = new CreateUserCommandHandler(
            context,
            hasher,
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            Options.Create(new LicencaOptions()),
            NullLogger<CreateUserCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreateUserCommand
        {
            Nome = "Usuario Duplicado",
            Email = "duplicado@email.com",
            Telefone = "+5511888888888",
            Cpf = "39864590827",
            DataNascimento = new DateTime(1995, 1, 20)
        }, CancellationToken.None));
    }

}
