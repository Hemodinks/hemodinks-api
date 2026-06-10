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
        catch (KeyNotFoundException)
        {
            return options.NotFoundMessage == null
                ? Results.NotFound()
                : Results.NotFound(new { message = options.NotFoundMessage });
        }
        catch (UnauthorizedAccessException ex)
        {
            if (options.UnauthorizedAccessAsUnauthorized)
            {
                logger.LogWarning(ex, logMessage);
                return Results.Unauthorized();
            }

            return Results.Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, logMessage);
            return Results.BadRequest(new { message = clientMessage, error = ex.Message });
        }
    }
}

public sealed class EndpointErrorOptions
{
    public static readonly EndpointErrorOptions Default = new();

    public bool UnauthorizedAccessAsUnauthorized { get; init; }

    public string? NotFoundMessage { get; init; }
}
