using HemodinksAPI.Application.Services;

namespace HemodinksAPI.Infrastructure.PasswordReset;

public class FunctionBackedPasswordResetNotificationSender : IPasswordResetNotificationTransport
{
    private readonly PasswordResetFunctionClient _client;

    public FunctionBackedPasswordResetNotificationSender(PasswordResetFunctionClient client)
    {
        _client = client;
    }

    public string Name => "Azure Function HTTP";

    public async Task<PasswordResetNotificationDispatchStatus> SendAsync(
        PasswordResetNotification notification,
        CancellationToken cancellationToken)
    {
        await _client.PostJsonAsync(
            "password-reset/send",
            new RemotePasswordResetEmailRequest(
                notification.Email,
                notification.Nome,
                notification.Token,
                notification.ExpiresAt),
            cancellationToken);

        return PasswordResetNotificationDispatchStatus.Sent;
    }

    private sealed record RemotePasswordResetEmailRequest(
        string Email,
        string Nome,
        string Token,
        DateTime ExpiresAt);
}
