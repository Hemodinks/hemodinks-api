using HemodinksAPI.Application.Async;

namespace HemodinksAPI.Infrastructure.Queues;

public class DisabledFileExportQueue : IFileExportQueue
{
    public Task EnqueueAsync(FileExportQueueMessage message, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("AsyncQueues:Enabled deve ser true para solicitar exportacoes PDF/XLSX.");
    }
}
