using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public partial class UserCommandHandlerTests
{
    [Fact]
    public async Task AuthenticateUser_WhenCredentialsAreValid_ReturnsTokenAndUserData()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        context.Users.Add(CreateUser(
            email: "login@email.com",
            passwordHash: hasher.HashPassword("Senha@123"),
            precisaTrocarSenha: true,
            fotoPerfil: "https://storage.example/login.png"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var handler = new AuthenticateUserCommandHandler(
            context,
            hasher,
            new StubJwtTokenService("fake-token"),
            CreateLicencaService(context),
            NullLogger<AuthenticateUserCommandHandler>.Instance);

        var response = await handler.Handle(new AuthenticateUserCommand
        {
            Email = "login@email.com",
            Senha = "Senha@123"
        }, CancellationToken.None);

        Assert.Equal("login@email.com", response.Email);
        Assert.Equal("fake-token", response.Token);
        Assert.Equal("https://storage.example/login.png", response.FotoPerfil);
        Assert.True(response.PrecisaTrocarSenha);
        Assert.Equal(Perfil.MedicosId, response.PerfilId);
        Assert.Equal("54321", response.Crm);
        Assert.Equal("SP", response.CrmUf);
        Assert.Equal("Médicos", response.PerfilNome);
        Assert.NotNull(response.Licenca);
        Assert.Equal(LicencaPlanos.Trial, response.Licenca.Plano);
        Assert.Contains(LicencaFeatures.PacientesVisualizar, response.Licenca.FeaturesEfetivas);
        Assert.DoesNotContain(LicencaFeatures.PacientesGerenciar, response.Licenca.FeaturesEfetivas);
    }

    [Fact]
    public async Task AuthenticateUser_WhenUserIsController_ReturnsPatientManagementLicense()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        context.Users.Add(CreateUser(
            email: "controller.login@email.com",
            passwordHash: hasher.HashPassword("Senha@123"),
            perfilId: Perfil.ControllerId));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var handler = new AuthenticateUserCommandHandler(
            context,
            hasher,
            new StubJwtTokenService("fake-token"),
            CreateLicencaService(context),
            NullLogger<AuthenticateUserCommandHandler>.Instance);

        var response = await handler.Handle(new AuthenticateUserCommand
        {
            Email = "controller.login@email.com",
            Senha = "Senha@123"
        }, CancellationToken.None);

        Assert.Equal(Perfil.ControllerId, response.PerfilId);
        Assert.NotNull(response.Licenca);
        Assert.False(response.Licenca.ControleAplicavel);
        Assert.True(response.Licenca.AcessoCompleto);
        Assert.Contains(LicencaFeatures.PacientesVisualizar, response.Licenca.FeaturesEfetivas);
        Assert.Contains(LicencaFeatures.PacientesGerenciar, response.Licenca.FeaturesEfetivas);
        Assert.Equal(0, await context.Licencas.CountAsync());
    }

    [Fact]
    public async Task UpdateUser_WhenPerfilIsValid_UpdatesPerfil()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 25,
            email: "edita@email.com",
            passwordHash: hasher.HashPassword("Senha@123"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new UpdateUserCommandHandler(
            context,
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            NullLogger<UpdateUserCommandHandler>.Instance);

        var response = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            Nome = "Usuario Editado",
            Email = "edita@email.com",
            Telefone = "+5511555555555",
            Cpf = "15350946056",
            FotoPerfil = "data:image/jpeg;base64,editada",
            DataNascimento = new DateTime(1991, 7, 2),
            Ativo = true,
            PerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal("https://storage.example/1.png", storedUser.FotoPerfil);
        Assert.Equal("https://storage.example/1.png", response.FotoPerfil);
        Assert.Equal(Perfil.AdministradorId, storedUser.PerfilId);
        Assert.Equal(Perfil.AdministradorId, response.PerfilId);
        Assert.Equal("Administrador", response.PerfilNome);
        Assert.Null(storedUser.Crm);
        Assert.Null(storedUser.CrmUf);
        Assert.Null(response.Crm);
        Assert.Null(response.CrmUf);
        Assert.NotNull(storedUser.DataAtualizacao);
        Assert.Equal(storedUser.DataAtualizacao, response.DataAtualizacao);
    }

    [Fact]
    public async Task UpdateUser_WhenDoctorUpdatesAnotherUser_ThrowsUnauthorizedAccessException()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 25,
            email: "edita.negada@email.com",
            passwordHash: hasher.HashPassword("Senha@123"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new UpdateUserCommandHandler(
            context,
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            NullLogger<UpdateUserCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            CurrentUser = new CurrentUserContext(99, Perfil.MedicosId, "Outro Medico"),
            Nome = "Usuario Editado",
            Email = "edita.negada@email.com",
            Telefone = "+5511555555555",
            Cpf = "15350946056",
            DataNascimento = new DateTime(1991, 7, 2),
            Ativo = true,
            PerfilId = Perfil.MedicosId
        }, CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticateUser_WhenPasswordIsInvalid_ThrowsUnauthorizedAccessException()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        context.Users.Add(CreateUser(
            email: "login@email.com",
            passwordHash: hasher.HashPassword("Senha@123")));
        await context.SaveChangesAsync();

        var handler = new AuthenticateUserCommandHandler(
            context,
            hasher,
            new StubJwtTokenService("fake-token"),
            CreateLicencaService(context),
            NullLogger<AuthenticateUserCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new AuthenticateUserCommand
        {
            Email = "login@email.com",
            Senha = "senha-errada"
        }, CancellationToken.None));
    }

}
