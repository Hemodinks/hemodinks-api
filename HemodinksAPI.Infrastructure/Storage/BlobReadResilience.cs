using Azure;
using Azure.Core;
using Azure.Storage.Blobs;

namespace HemodinksAPI.Infrastructure.Storage;

/// <summary>
/// Limita o tempo de leitura de blobs para que uma lentidão ou throttling do Azure Storage
/// não segure a requisição por minutos (incidente de 04/11/2026: ~200 s por foto).
/// Uploads e exclusões continuam com as opções padrão do SDK.
/// </summary>
internal static class BlobReadResilience
{
    public const int DefaultReadTimeoutSeconds = 8;

    public static TimeSpan ResolveTimeout(int readTimeoutSeconds)
    {
        return TimeSpan.FromSeconds(readTimeoutSeconds > 0 ? readTimeoutSeconds : DefaultReadTimeoutSeconds);
    }

    public static BlobClientOptions CreateReadClientOptions(TimeSpan readTimeout)
    {
        var options = new BlobClientOptions();
        options.Retry.Mode = RetryMode.Exponential;
        options.Retry.MaxRetries = 1;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
        options.Retry.MaxDelay = TimeSpan.FromSeconds(1);
        options.Retry.NetworkTimeout = readTimeout;
        return options;
    }

    /// <summary>
    /// Falha de leitura do Storage que não foi causada pelo cancelamento do próprio chamador.
    /// </summary>
    public static bool IsReadFailure(Exception exception, CancellationToken callerToken)
    {
        return exception switch
        {
            RequestFailedException => true,
            AggregateException => true,
            TimeoutException => true,
            OperationCanceledException => !callerToken.IsCancellationRequested,
            _ => false
        };
    }

    public static string Describe(Exception exception)
    {
        return exception is RequestFailedException requestFailed
            ? $"status {requestFailed.Status}, ErrorCode {requestFailed.ErrorCode ?? "n/d"}"
            : exception.GetType().Name;
    }
}
