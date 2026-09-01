using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Services;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public partial class UserCommandHandlerTests
{
    [Fact]
    public async Task DeleteUser_InactivatesUserAndMembershipWithoutRemovingHistory()
    {
        await using var context = TestDbContextFactory.Create();
        var user = CreateUser(
            email: "inativar@email.com",
            passwordHash: new PasswordHasher().HashPassword("TestPassword@123"),
            perfilId: Perfil.AdministradorId);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        await GlobalIdentityService.EnsureForUserAsync(context, user, CancellationToken.None);

        var handler = new DeleteUserCommandHandler(
            context,
            NullLogger<DeleteUserCommandHandler>.Instance);

        await handler.Handle(new DeleteUserCommand
        {
            Id = user.Id,
            CurrentUser = new CurrentUserContext(99, Perfil.SuperAdministradorId, "Super Admin")
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync(item => item.Id == user.Id);
        var membership = await context.UsuariosClinicas.SingleAsync(item => item.UserId == user.Id);
        Assert.False(storedUser.Ativo);
        Assert.NotNull(storedUser.DataAtualizacao);
        Assert.False(membership.Ativo);
        Assert.Equal(1, await context.Users.CountAsync());
    }

    [Fact]
    public async Task DeleteUser_WhenAdministratorTargetsSuperAdministrator_RejectsOperation()
    {
        await using var context = TestDbContextFactory.Create();
        var user = CreateUser(
            email: "superadmin@email.com",
            passwordHash: new PasswordHasher().HashPassword("TestPassword@123"),
            perfilId: Perfil.SuperAdministradorId);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new DeleteUserCommandHandler(
            context,
            NullLogger<DeleteUserCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new DeleteUserCommand
        {
            Id = user.Id,
            CurrentUser = new CurrentUserContext(99, Perfil.AdministradorId, "Admin")
        }, CancellationToken.None));

        Assert.True((await context.Users.SingleAsync(item => item.Id == user.Id)).Ativo);
    }

    [Fact]
    public async Task CreateUser_WhenEmailBelongsToInactiveUser_ReactivatesExistingRegistration()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            email: "reativar@email.com",
            passwordHash: hasher.HashPassword("TestPassword@123"),
            perfilId: Perfil.AdministradorId);
        user.Ativo = false;
        context.Users.Add(user);
        await context.SaveChangesAsync();
        await GlobalIdentityService.EnsureForUserAsync(context, user, CancellationToken.None);
        var originalId = user.Id;

        var handler = new CreateUserCommandHandler(
            context,
            hasher,
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            Options.Create(new LicencaOptions()),
            NullLogger<CreateUserCommandHandler>.Instance);

        var response = await handler.Handle(new CreateUserCommand
        {
            CurrentUser = new CurrentUserContext(99, Perfil.AdministradorId, "Admin"),
            Nome = "Usuario Reativado",
            Email = "reativar@email.com",
            Telefone = "+5511888888888",
            Cpf = "15350946056",
            PerfilId = Perfil.ControllerId
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        var membership = await context.UsuariosClinicas
            .Include(item => item.UsuarioGlobal)
            .SingleAsync(item => item.UserId == originalId);
        Assert.Equal(originalId, response.Id);
        Assert.Equal("Usuario Reativado", storedUser.Nome);
        Assert.True(storedUser.Ativo);
        Assert.True(storedUser.PrecisaTrocarSenha);
        Assert.True(membership.Ativo);
        Assert.True(membership.UsuarioGlobal.Ativo);
        Assert.Equal(Perfil.ControllerId, membership.PerfilId);
    }
}
