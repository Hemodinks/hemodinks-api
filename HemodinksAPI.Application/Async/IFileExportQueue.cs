namespace HemodinksAPI.Application.Async;

public interface IFileExportQueue
{
    Task EnqueueAsync(FileExportQueueMessage message, CancellationToken cancellationToken);
}
