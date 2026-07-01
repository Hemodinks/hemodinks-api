using HemodinksAPI.Application.Async;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Queues;

public class AzureFileExportQueue : IFileExportQueue
{
    private readonly IAsyncQueuePublisher _queuePublisher;
    private readonly AsyncQueueOptions _options;

    public AzureFileExportQueue(
        IAsyncQueuePublisher queuePublisher,
        IOptions<AsyncQueueOptions> options)
    {
        _queuePublisher = queuePublisher;
        _options = options.Value;
    }

    public Task EnqueueAsync(FileExportQueueMessage message, CancellationToken cancellationToken)
    {
        return _queuePublisher.EnqueueAsync(_options.FileExportQueueName, message, cancellationToken);
    }
}
