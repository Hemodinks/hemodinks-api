namespace HemodinksAPI.Api;

public static class EndpointExecution
{
    public static async Task<IResult> RunAsync(
        Func<Task<IResult>> action,
        ILogger logger,
        string logMessage,
        string clientMessage,
        EndpointErrorOptions? options = null)
    {
        options ??= EndpointErrorOptions.Default;

        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            logger.Log(
                ApiExceptionResults.IsExpected(ex) ? LogLevel.Warning : LogLevel.Error,
                ex,
                logMessage);
            return ApiExceptionResults.Map(ex, options with { InternalServerErrorTitle = clientMessage });
        }
    }
}

public sealed record EndpointErrorOptions
{
    public static readonly EndpointErrorOptions Default = new();

    public bool UnauthorizedAccessAsUnauthorized { get; init; }

    public string? NotFoundMessage { get; init; }

    public bool NotFoundUsesExceptionMessage { get; init; }

    public bool ConcurrencyUsesExceptionMessage { get; init; }

    public string InternalServerErrorTitle { get; init; } = "Erro interno ao processar a requisicao";
}

