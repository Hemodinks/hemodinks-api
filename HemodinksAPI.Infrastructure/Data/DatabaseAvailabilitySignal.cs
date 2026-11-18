namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Marca, uma vez por processo, que o banco ja respondeu a uma conexao real (trafego, warmup ou /healthz).
/// Tarefas de fundo esperam este sinal em vez de conectar no arranque: assim nao acordam o SQL serverless
/// por conta propria nem disputam a conexao enquanto ele ainda esta retomando.
/// </summary>
public sealed class DatabaseAvailabilitySignal
{
    private readonly TaskCompletionSource _available = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsAvailable => _available.Task.IsCompleted;

    public void MarkAvailable() => _available.TrySetResult();

    public Task WaitAsync(CancellationToken cancellationToken) => _available.Task.WaitAsync(cancellationToken);
}
