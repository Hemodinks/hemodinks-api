using System.Text.Json;
using Azure.Storage.Queues;
using HemodinksAPI.Application.Async;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Queues;

public class AzureStorageQueuePublisher : IAsyncQueuePublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AsyncQueueOptions _options;
    private readonly ILogger<AzureStorageQueuePublisher> _logger;

    public AzureStorageQueuePublisher(
        IOptions<AsyncQueueOptions> options,
        ILogger<AzureStorageQueuePublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnqueueAsync<TMessage>(string queueName, TMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("AsyncQueues:ConnectionString ou AzureStorage:ConnectionString deve ser configurado para usar filas.");
        }

        var normalizedQueueName = NormalizeQueueName(queueName);
        var queueClient = new QueueClient(_options.ConnectionString, normalizedQueueName, new QueueClientOptions
        {
            MessageEncoding = QueueMessageEncoding.Base64
        });

        await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var payload = JsonSerializer.Serialize(message, JsonOptions);
        await queueClient.SendMessageAsync(payload, cancellationToken);

        _logger.LogInformation("Mensagem enfileirada na fila {QueueName}", normalizedQueueName);
    }

    private static string NormalizeQueueName(string queueName)
    {
        if (string.IsNullOrWhiteSpace(queueName))
        {
            throw new InvalidOperationException("Nome da fila obrigatorio");
        }

        var normalized = queueName.Trim().ToLowerInvariant();
        if (normalized.Length is < 3 or > 63
            || normalized.StartsWith('-')
            || normalized.EndsWith('-')
            || normalized.Contains("--", StringComparison.Ordinal)
            || normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            throw new InvalidOperationException("Nome da fila Azure invalido");
        }

        return normalized;
    }
}
