using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class UserCommandHandlerTests
{
    [Fact]
    public async Task ChangePassword_WhenCurrentPasswordIsValid_UpdatesHashAndClearsChangeFlag()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 42,
            email: "troca@email.com",
            passwordHash: hasher.HashPassword("Senha@123"),
            precisaTrocarSenha: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new ChangePasswordCommandHandler(
            context,
            hasher,
            NullLogger<ChangePasswordCommandHandler>.Instance);

        var response = await handler.Handle(new ChangePasswordCommand
        {
            UserId = user.Id,
            SenhaAtual = "Senha@123",
            NovaSenha = "NovaSenha@123"
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        Assert.Equal(user.Id, response.Id);
        Assert.False(response.PrecisaTrocarSenha);
        Assert.False(storedUser.PrecisaTrocarSenha);
        Assert.True(hasher.VerifyPassword("NovaSenha@123", storedUser.Senha));
        Assert.False(hasher.VerifyPassword("Senha@123", storedUser.Senha));
    }

    [Fact]
    public async Task ChangePassword_WhenCurrentUserDoesNotMatchRouteUser_ThrowsUnauthorizedAccessException()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new ChangePasswordCommandHandler(
            context,
            new PasswordHasher(),
            NullLogger<ChangePasswordCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new ChangePasswordCommand
        {
            UserId = 10,
            CurrentUser = new CurrentUserContext(99, Perfil.MedicosId, "Outro Medico"),
            SenhaAtual = "Senha@123",
            NovaSenha = "NovaSenha@123"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_WhenCurrentPasswordIsInvalid_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 11,
            email: "senha.invalida@email.com",
            passwordHash: hasher.HashPassword("Senha@123"),
            precisaTrocarSenha: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new ChangePasswordCommandHandler(
            context,
            hasher,
            NullLogger<ChangePasswordCommandHandler>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new ChangePasswordCommand
        {
            UserId = user.Id,
            SenhaAtual = "SenhaErrada@123",
            NovaSenha = "NovaSenha@123"
        }, CancellationToken.None));

        Assert.Equal("Senha atual invalida", exception.Message);
    }

    [Fact]
    public async Task ChangePassword_WhenNewPasswordIsTooShort_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 1,
            email: "senha.curta@email.com",
            passwordHash: hasher.HashPassword("SenhaAtual@123"),
            precisaTrocarSenha: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var handler = new ChangePasswordCommandHandler(
            context,
            hasher,
            NullLogger<ChangePasswordCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new ChangePasswordCommand
        {
            UserId = 1,
            SenhaAtual = "SenhaAtual@123",
            NovaSenha = "curta"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_WhenNewPasswordMatchesCurrentPassword_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 10,
            email: "mesma.senha@email.com",
            passwordHash: hasher.HashPassword("SenhaAtual@123"),
            precisaTrocarSenha: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new ChangePasswordCommandHandler(
            context,
            hasher,
            NullLogger<ChangePasswordCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new ChangePasswordCommand
        {
            UserId = user.Id,
            SenhaAtual = "SenhaAtual@123",
            NovaSenha = "SenhaAtual@123"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task ResetUserPassword_WhenUserExists_SetsTemporaryPasswordAndRequiresPasswordChange()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 7,
            email: "reset@email.com",
            passwordHash: hasher.HashPassword("SenhaAntiga@123"),
            precisaTrocarSenha: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new ResetUserPasswordCommandHandler(
            context,
            hasher,
            NullLogger<ResetUserPasswordCommandHandler>.Instance);

        var response = await handler.Handle(new ResetUserPasswordCommand
        {
            UserId = user.Id
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        Assert.Equal(user.Id, response.Id);
        Assert.True(response.PrecisaTrocarSenha);
        Assert.True(storedUser.PrecisaTrocarSenha);
        Assert.NotNull(response.SenhaTemporaria);
        Assert.True(hasher.VerifyPassword(response.SenhaTemporaria, storedUser.Senha));
        Assert.False(hasher.VerifyPassword("SenhaAntiga@123", storedUser.Senha));
    }

    [Fact]
    public async Task ResetUserPassword_WhenUserDoesNotExist_ThrowsKeyNotFoundException()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new ResetUserPasswordCommandHandler(
            context,
            new PasswordHasher(),
            NullLogger<ResetUserPasswordCommandHandler>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(new ResetUserPasswordCommand
        {
            UserId = 999
        }, CancellationToken.None));
    }

}
