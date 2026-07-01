using System.Text.Json;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Workers.Functions;

public class PasswordResetEmailFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SmtpPasswordResetNotificationSender _sender;
    private readonly ILogger<PasswordResetEmailFunction> _logger;

    public PasswordResetEmailFunction(
        SmtpPasswordResetNotificationSender sender,
        ILogger<PasswordResetEmailFunction> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    [Function(nameof(SendPasswordResetEmail))]
    public async Task SendPasswordResetEmail(
        [QueueTrigger("%PasswordResetEmailQueueName%", Connection = "AzureWebJobsStorage")] string queueMessage,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<PasswordResetEmailQueueMessage>(queueMessage, JsonOptions)
            ?? throw new InvalidOperationException("Mensagem de reset de senha invalida");

        await _sender.SendAsync(new PasswordResetNotification(
            message.Email,
            message.Nome,
            message.Token,
            message.ExpiresAt), cancellationToken);

        _logger.LogInformation("Email de reset de senha processado para {Email}", message.Email);
    }
}
