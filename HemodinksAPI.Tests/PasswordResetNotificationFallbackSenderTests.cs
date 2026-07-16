using HemodinksAPI.Application.Services;
using HemodinksAPI.Infrastructure.PasswordReset;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class PasswordResetNotificationFallbackSenderTests
{
    [Fact]
    public async Task SendAsync_WhenFirstTransportFails_UsesNextTransport()
    {
        var failingTransport = new FakePasswordResetTransport(
            "Function",
            exceptionToThrow: new InvalidOperationException("function antiga"));
        var smtpTransport = new FakePasswordResetTransport("SMTP");
        var sender = new FallbackPasswordResetNotificationSender(
            new IPasswordResetNotificationTransport[] { failingTransport, smtpTransport },
            NullLogger<FallbackPasswordResetNotificationSender>.Instance);

        var status = await sender.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(PasswordResetNotificationDispatchStatus.Sent, status);
        Assert.Equal(1, failingTransport.Attempts);
        Assert.Equal(1, smtpTransport.Attempts);
    }

    [Fact]
    public async Task SendAsync_WhenAllTransportsFail_ThrowsClearError()
    {
        var sender = new FallbackPasswordResetNotificationSender(
            new IPasswordResetNotificationTransport[]
            {
                new FakePasswordResetTransport("Function", new InvalidOperationException("function antiga")),
                new FakePasswordResetTransport("Queue", new InvalidOperationException("storage antiga"))
            },
            NullLogger<FallbackPasswordResetNotificationSender>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync(CreateNotification(), CancellationToken.None));

        Assert.Equal("Nenhum canal configurado conseguiu enviar reset de senha.", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static PasswordResetNotification CreateNotification()
    {
        return new PasswordResetNotification(
            "usuario@email.com",
            "Usuario",
            "token",
            DateTime.UtcNow.AddMinutes(30));
    }

    private sealed class FakePasswordResetTransport : IPasswordResetNotificationTransport
    {
        private readonly Exception? _exceptionToThrow;

        public FakePasswordResetTransport(
            string name,
            Exception? exceptionToThrow = null,
            PasswordResetNotificationDispatchStatus dispatchStatus = PasswordResetNotificationDispatchStatus.Sent)
        {
            Name = name;
            _exceptionToThrow = exceptionToThrow;
            DispatchStatus = dispatchStatus;
        }

        public string Name { get; }

        public int Attempts { get; private set; }

        public PasswordResetNotificationDispatchStatus DispatchStatus { get; }

        public Task<PasswordResetNotificationDispatchStatus> SendAsync(
            PasswordResetNotification notification,
            CancellationToken cancellationToken)
        {
            Attempts++;

            if (_exceptionToThrow is not null)
            {
                throw _exceptionToThrow;
            }

            return Task.FromResult(DispatchStatus);
        }
    }
}
