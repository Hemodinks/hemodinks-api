using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Workers.Functions;

public sealed class EventEmailFunction(SmtpEventEmailSender sender, ILogger<EventEmailFunction> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    [Function(nameof(SendEventEmail))]
    public async Task<HttpResponseData> SendEventEmail(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "event-email/send")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        EventEmailMessage? message;
        try
        {
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (body.Length + read > 24 * 1024) return request.CreateResponse(HttpStatusCode.BadRequest);
                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            body.Position = 0;
            message = await JsonSerializer.DeserializeAsync<EventEmailMessage>(body, JsonOptions, cancellationToken);
        }
        catch (JsonException) { return request.CreateResponse(HttpStatusCode.BadRequest); }
        if (message == null || !message.IsValid()) return request.CreateResponse(HttpStatusCode.BadRequest);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await sender.SendAsync(message, timeout.Token);
            return request.CreateResponse(HttpStatusCode.OK);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Falha no envio de email pela Function: {ErrorType}", exception.GetType().Name);
            return request.CreateResponse(HttpStatusCode.ServiceUnavailable);
        }
    }
}
