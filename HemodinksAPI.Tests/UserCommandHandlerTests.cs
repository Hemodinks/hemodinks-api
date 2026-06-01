using HemodinksAPI.Api.Authentication;
using HemodinksAPI.Api.Features.Users.Commands;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class UserCommandHandlerTests
{
    [Fact]
    public async Task CreateUser_WhenEmailIsNew_CreatesActiveUserWithDefaultPassword()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var handler = new CreateUserCommandHandler(
            context,
            hasher,
            NullLogger<CreateUserCommandHandler>.Instance);

        var response = await handler.Handle(new CreateUserCommand
        {
            Nome = "Novo Usuario",
            Email = "novo.usuario@email.com",
            Telefone = "+5511999999999",
            DataNascimento = new DateTime(1990, 5, 15)
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        Assert.Equal(storedUser.Id, response.Id);
        Assert.Equal("Novo Usuario", storedUser.Nome);
        Assert.True(storedUser.Ativo);
        Assert.True(storedUser.PrecisaTrocarSenha);
        Assert.True(response.PrecisaTrocarSenha);
        Assert.True(hasher.VerifyPassword(DefaultUserPassword.Value, storedUser.Senha));
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
            NullLogger<CreateUserCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreateUserCommand
        {
            Nome = "Usuario Duplicado",
            Email = "duplicado@email.com",
            Telefone = "+5511888888888",
            DataNascimento = new DateTime(1995, 1, 20)
        }, CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticateUser_WhenCredentialsAreValid_ReturnsTokenAndUserData()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        context.Users.Add(CreateUser(
            email: "login@email.com",
            passwordHash: hasher.HashPassword("Senha@123"),
            precisaTrocarSenha: true));
        await context.SaveChangesAsync();

        var handler = new AuthenticateUserCommandHandler(
            context,
            hasher,
            new StubJwtTokenService("fake-token"),
            NullLogger<AuthenticateUserCommandHandler>.Instance);

        var response = await handler.Handle(new AuthenticateUserCommand
        {
            Email = "login@email.com",
            Senha = "Senha@123"
        }, CancellationToken.None);

        Assert.Equal("login@email.com", response.Email);
        Assert.Equal("fake-token", response.Token);
        Assert.True(response.PrecisaTrocarSenha);
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
            NullLogger<AuthenticateUserCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new AuthenticateUserCommand
        {
            Email = "login@email.com",
            Senha = "senha-errada"
        }, CancellationToken.None));
    }

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
    public async Task ChangePassword_WhenNewPasswordIsDefault_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var handler = new ChangePasswordCommandHandler(
            context,
            hasher,
            NullLogger<ChangePasswordCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new ChangePasswordCommand
        {
            UserId = 1,
            SenhaAtual = "Senha@123",
            NovaSenha = DefaultUserPassword.Value
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
    public async Task ResetUserPassword_WhenUserExists_SetsDefaultPasswordAndRequiresPasswordChange()
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
        Assert.True(hasher.VerifyPassword(DefaultUserPassword.Value, storedUser.Senha));
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

    private static User CreateUser(
        string email,
        string passwordHash,
        int id = 0,
        bool precisaTrocarSenha = true)
    {
        return new User
        {
            Id = id,
            Nome = "Usuario Teste",
            Email = email,
            Telefone = "+5511999999999",
            Senha = passwordHash,
            DataCadastro = DateTime.UtcNow,
            DataNascimento = new DateTime(1990, 1, 1),
            Ativo = true,
            PrecisaTrocarSenha = precisaTrocarSenha
        };
    }

    private sealed class StubJwtTokenService : IJwtTokenService
    {
        private readonly string _token;

        public StubJwtTokenService(string token)
        {
            _token = token;
        }

        public string GenerateToken(User user)
        {
            return _token;
        }
    }
}
