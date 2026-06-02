using HemodinksAPI.Api.Authentication;
using HemodinksAPI.Api.Features.Users.Commands;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Services;
using HemodinksAPI.Api.Storage;
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
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            NullLogger<CreateUserCommandHandler>.Instance);

        var response = await handler.Handle(new CreateUserCommand
        {
            Nome = "Novo Usuario",
            Email = "novo.usuario@email.com",
            Telefone = "+5511999999999",
            Cpf = "52998224725",
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
        Assert.Equal("Médicos", response.PerfilNome);
        Assert.True(hasher.VerifyPassword(DefaultUserPassword.Value, storedUser.Senha));
    }

    [Fact]
    public async Task CreateUser_WhenPerfilIsProvided_AssignsPerfil()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var handler = new CreateUserCommandHandler(
            context,
            hasher,
            new FakeProfilePhotoStorage(),
            new UserPatientSyncService(context),
            NullLogger<CreateUserCommandHandler>.Instance);

        var response = await handler.Handle(new CreateUserCommand
        {
            Nome = "Paciente Teste",
            Email = "paciente@email.com",
            Telefone = "+5511777777777",
            Cpf = "11144477735",
            DataNascimento = new DateTime(1992, 8, 10),
            PerfilId = Perfil.PacientesId
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        var storedPaciente = await context.Pacientes.SingleAsync();
        Assert.Equal(Perfil.PacientesId, storedUser.PerfilId);
        Assert.Equal(storedUser.Id, storedPaciente.UserId);
        Assert.Equal("Paciente Teste", storedPaciente.NomePaciente);
        Assert.Equal(Perfil.PacientesId, response.PerfilId);
        Assert.Equal("Pacientes", response.PerfilNome);
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
        Assert.Equal("Médicos", response.PerfilNome);
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

    [Fact]
    public async Task ResetUserPasswordByEmail_WhenUserExists_SetsDefaultPasswordAndRequiresPasswordChange()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 8,
            email: "reset-email@email.com",
            passwordHash: hasher.HashPassword("SenhaAntiga@123"),
            precisaTrocarSenha: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new ResetUserPasswordByEmailCommandHandler(
            context,
            hasher,
            NullLogger<ResetUserPasswordByEmailCommandHandler>.Instance);

        var response = await handler.Handle(new ResetUserPasswordByEmailCommand
        {
            Email = "reset-email@email.com"
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        Assert.Equal(user.Id, response.Id);
        Assert.True(response.PrecisaTrocarSenha);
        Assert.True(storedUser.PrecisaTrocarSenha);
        Assert.True(hasher.VerifyPassword(DefaultUserPassword.Value, storedUser.Senha));
        Assert.False(hasher.VerifyPassword("SenhaAntiga@123", storedUser.Senha));
    }

    private static User CreateUser(
        string email,
        string passwordHash,
        int id = 0,
        bool precisaTrocarSenha = true,
        string? fotoPerfil = null)
    {
        return new User
        {
            Id = id,
            Nome = "Usuario Teste",
            Email = email,
            Telefone = "+5511999999999",
            Cpf = "52998224725",
            Senha = passwordHash,
            DataCadastro = DateTime.UtcNow,
            DataNascimento = new DateTime(1990, 1, 1),
            Ativo = true,
            PrecisaTrocarSenha = precisaTrocarSenha,
            FotoPerfil = fotoPerfil,
            PerfilId = Perfil.MedicosId
        };
    }

    private sealed class FakeProfilePhotoStorage : IProfilePhotoStorage
    {
        private int _saveCount;

        public Task<string?> SaveAsync(string? fotoPerfil, string? currentFotoPerfil, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(fotoPerfil))
            {
                return Task.FromResult<string?>(null);
            }

            if (fotoPerfil.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                _saveCount++;
                return Task.FromResult<string?>($"https://storage.example/{_saveCount}.png");
            }

            return Task.FromResult<string?>(fotoPerfil);
        }

        public Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
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
