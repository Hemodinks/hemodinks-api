using System.Net;
using System.Text.Json;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
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
            message.ExpiresAt,
            message.ClinicaId), cancellationToken);

        _logger.LogInformation(
            "Email de reset de senha processado para {MaskedEmail} na clinica {ClinicaId}",
            HemodinksAPI.Application.Security.SensitiveDataMasking.MaskEmail(message.Email),
            message.ClinicaId);
    }

    [Function(nameof(SendPasswordResetEmailSync))]
    public async Task<HttpResponseData> SendPasswordResetEmailSync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "password-reset/send")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var payload = await JsonSerializer.DeserializeAsync<PasswordResetEmailRequest>(
            request.Body,
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("Payload de reset de senha invalido.");

        await _sender.SendAsync(new PasswordResetNotification(
            payload.Email,
            payload.Nome,
            payload.Token,
            payload.ExpiresAt,
            payload.ClinicaId > 0 ? payload.ClinicaId : 1), cancellationToken);

        _logger.LogInformation(
            "Email de reset de senha enviado pela function HTTP para {MaskedEmail}",
            HemodinksAPI.Application.Security.SensitiveDataMasking.MaskEmail(payload.Email));

        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(new { sent = true }, JsonOptions),
            cancellationToken);
        return response;
    }

    private sealed record PasswordResetEmailRequest(
        string Email,
        string Nome,
        string Token,
        DateTime ExpiresAt,
        int ClinicaId = 1);
}
