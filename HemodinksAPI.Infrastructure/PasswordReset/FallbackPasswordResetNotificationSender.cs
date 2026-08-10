namespace HemodinksAPI.Infrastructure.PasswordReset;

public class FallbackPasswordResetNotificationSender : IPasswordResetNotificationSender
{
    private readonly IEnumerable<IPasswordResetNotificationTransport> _transports;
    private readonly ILogger<FallbackPasswordResetNotificationSender> _logger;

    public FallbackPasswordResetNotificationSender(
        IEnumerable<IPasswordResetNotificationTransport> transports,
        ILogger<FallbackPasswordResetNotificationSender> logger)
    {
        _transports = transports;
        _logger = logger;
    }

    public async Task<PasswordResetNotificationDispatchStatus> SendAsync(
        PasswordResetNotification notification,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        var attemptedTransport = false;

        foreach (var transport in _transports)
        {
            attemptedTransport = true;

            try
            {
                return await transport.SendAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex,
                    "Falha ao enviar reset de senha via {Transport}. Tentando proximo canal configurado.",
                    transport.Name);
            }
        }

        var message = attemptedTransport
            ? "Nenhum canal configurado conseguiu enviar reset de senha."
            : "Nenhum canal de envio de reset de senha foi configurado.";

        throw new InvalidOperationException(message, lastException);
    }
}
