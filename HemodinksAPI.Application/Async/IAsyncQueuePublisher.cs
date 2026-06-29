namespace HemodinksAPI.Application.Async;

public interface IAsyncQueuePublisher
{
    Task EnqueueAsync<TMessage>(string queueName, TMessage message, CancellationToken cancellationToken);
}
