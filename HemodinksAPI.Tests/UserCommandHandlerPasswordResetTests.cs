using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public partial class UserCommandHandlerTests
{
    [Fact]
    public async Task ResetUserPasswordByEmail_WhenUserExists_CreatesTokenWithoutChangingPassword()
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

        var passwordResetSender = new FakePasswordResetNotificationSender();
        var handler = new ResetUserPasswordByEmailCommandHandler(
            context,
            hasher,
            passwordResetSender,
            Options.Create(new PasswordResetOptions { ExposeTokenInResponse = true }),
            NullLogger<ResetUserPasswordByEmailCommandHandler>.Instance);

        var response = await handler.Handle(new ResetUserPasswordByEmailCommand
        {
            Email = "reset-email@email.com"
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        Assert.False(storedUser.PrecisaTrocarSenha);
        Assert.True(hasher.VerifyPassword("SenhaAntiga@123", storedUser.Senha));
        Assert.NotNull(response.DebugToken);
        Assert.NotNull(response.ExpiresAt);
        Assert.Equal("email-token", response.Mode);
        Assert.Equal(
            "Enviamos um email com o link para redefinir sua senha. Use o link recebido para cadastrar uma nova senha.",
            response.Message);
        Assert.Equal(1, await context.PasswordResetTokens.CountAsync());
        Assert.Single(passwordResetSender.Notifications);
        Assert.Equal("reset-email@email.com", passwordResetSender.Notifications[0].Email);
    }

    [Fact]
    public async Task ResetUserPasswordByEmail_WhenNotificationIsOnlyQueued_ReturnsGenericMessage()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 81,
            email: "reset-queue@email.com",
            passwordHash: hasher.HashPassword("SenhaAntiga@123"),
            precisaTrocarSenha: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var passwordResetSender = new FakePasswordResetNotificationSender
        {
            DispatchStatus = PasswordResetNotificationDispatchStatus.Queued
        };
        var handler = new ResetUserPasswordByEmailCommandHandler(
            context,
            hasher,
            passwordResetSender,
            Options.Create(new PasswordResetOptions { ExposeTokenInResponse = true }),
            NullLogger<ResetUserPasswordByEmailCommandHandler>.Instance);

        var response = await handler.Handle(new ResetUserPasswordByEmailCommand
        {
            Email = "reset-queue@email.com"
        }, CancellationToken.None);

        Assert.Equal("email-token", response.Mode);
        Assert.Equal(
            "Recebemos sua solicitacao. Se o email estiver cadastrado, enviaremos as instrucoes para redefinir a senha.",
            response.Message);
        Assert.NotNull(response.DebugToken);
        Assert.Single(passwordResetSender.Notifications);
    }

    [Fact]
    public async Task ConfirmPasswordReset_WhenTokenIsValid_ChangesPassword()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 9,
            email: "confirm-reset@email.com",
            passwordHash: hasher.HashPassword("SenhaAntiga@123"),
            precisaTrocarSenha: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var requestHandler = new ResetUserPasswordByEmailCommandHandler(
            context,
            hasher,
            new FakePasswordResetNotificationSender(),
            Options.Create(new PasswordResetOptions { ExposeTokenInResponse = true }),
            NullLogger<ResetUserPasswordByEmailCommandHandler>.Instance);

        var requestResponse = await requestHandler.Handle(new ResetUserPasswordByEmailCommand
        {
            Email = "confirm-reset@email.com"
        }, CancellationToken.None);

        var confirmHandler = new ConfirmPasswordResetCommandHandler(
            context,
            hasher,
            NullLogger<ConfirmPasswordResetCommandHandler>.Instance);

        var response = await confirmHandler.Handle(new ConfirmPasswordResetCommand
        {
            Token = requestResponse.DebugToken!,
            NovaSenha = "NovaSenha@123"
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        var storedToken = await context.PasswordResetTokens.SingleAsync();
        Assert.Equal(user.Id, response.Id);
        Assert.False(response.PrecisaTrocarSenha);
        Assert.False(storedUser.PrecisaTrocarSenha);
        Assert.True(hasher.VerifyPassword("NovaSenha@123", storedUser.Senha));
        Assert.False(hasher.VerifyPassword("SenhaAntiga@123", storedUser.Senha));
        Assert.NotNull(storedToken.UsedAt);
    }

    [Fact]
    public async Task ResetUserPasswordByEmail_WhenEmailDoesNotExist_ReturnsGenericResponse()
    {
        await using var context = TestDbContextFactory.Create();
        var passwordResetSender = new FakePasswordResetNotificationSender();
        var handler = new ResetUserPasswordByEmailCommandHandler(
            context,
            new PasswordHasher(),
            passwordResetSender,
            Options.Create(new PasswordResetOptions { ExposeTokenInResponse = true }),
            NullLogger<ResetUserPasswordByEmailCommandHandler>.Instance);

        var response = await handler.Handle(new ResetUserPasswordByEmailCommand
        {
            Email = "nao-existe@email.com"
        }, CancellationToken.None);

        Assert.Null(response.DebugToken);
        Assert.NotNull(response.ExpiresAt);
        Assert.Equal(0, await context.PasswordResetTokens.CountAsync());
        Assert.Empty(passwordResetSender.Notifications);
    }

    [Fact]
    public async Task ResetUserPasswordByEmail_WhenUseEmailIsFalse_ResetsToDefaultPasswordWithoutSendingEmail()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 18,
            email: "reset-legado@email.com",
            passwordHash: hasher.HashPassword("SenhaAntiga@123"),
            precisaTrocarSenha: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var passwordResetSender = new FakePasswordResetNotificationSender();
        var handler = new ResetUserPasswordByEmailCommandHandler(
            context,
            hasher,
            passwordResetSender,
            Options.Create(new PasswordResetOptions { UseEmail = false }),
            NullLogger<ResetUserPasswordByEmailCommandHandler>.Instance);

        var response = await handler.Handle(new ResetUserPasswordByEmailCommand
        {
            Email = "reset-legado@email.com"
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        Assert.Equal(user.Id, response.Id);
        Assert.True(response.PrecisaTrocarSenha);
        Assert.Equal("default-password", response.Mode);
        Assert.Equal("Senha resetada para a senha padrao", response.Message);
        Assert.True(storedUser.PrecisaTrocarSenha);
        Assert.True(hasher.VerifyPassword(DefaultUserPassword.Value, storedUser.Senha));
        Assert.False(hasher.VerifyPassword("SenhaAntiga@123", storedUser.Senha));
        Assert.Equal(0, await context.PasswordResetTokens.CountAsync());
        Assert.Empty(passwordResetSender.Notifications);
    }

    [Fact]
    public async Task ResetUserPasswordByEmail_WhenNotificationFails_FallsBackToDefaultPassword()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var user = CreateUser(
            id: 28,
            email: "reset-fallback@email.com",
            passwordHash: hasher.HashPassword("SenhaAntiga@123"),
            precisaTrocarSenha: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var passwordResetSender = new FakePasswordResetNotificationSender
        {
            ExceptionToThrow = new InvalidOperationException("smtp indisponivel")
        };
        var handler = new ResetUserPasswordByEmailCommandHandler(
            context,
            hasher,
            passwordResetSender,
            Options.Create(new PasswordResetOptions { ExposeTokenInResponse = true }),
            NullLogger<ResetUserPasswordByEmailCommandHandler>.Instance);

        var response = await handler.Handle(new ResetUserPasswordByEmailCommand
        {
            Email = "reset-fallback@email.com"
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        var storedToken = await context.PasswordResetTokens.SingleAsync();
        Assert.Equal(user.Id, response.Id);
        Assert.True(response.PrecisaTrocarSenha);
        Assert.Equal("default-password", response.Mode);
        Assert.Equal(
            "Nao foi possivel enviar o email de redefinicao agora. A senha padrao foi aplicada para voce entrar e trocar a seguir.",
            response.Message);
        Assert.True(storedUser.PrecisaTrocarSenha);
        Assert.True(hasher.VerifyPassword(DefaultUserPassword.Value, storedUser.Senha));
        Assert.False(hasher.VerifyPassword("SenhaAntiga@123", storedUser.Senha));
        Assert.NotNull(storedToken.UsedAt);
        Assert.Single(passwordResetSender.Notifications);
    }

}
