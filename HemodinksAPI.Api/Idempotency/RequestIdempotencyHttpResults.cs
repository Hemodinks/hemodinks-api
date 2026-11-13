namespace HemodinksAPI.Api;

public static class RequestIdempotencyHttpResults
{
    public static IResult ToFailureResult<TResponse>(RequestIdempotencyExecutionResult<TResponse> execution)
    {
        return execution.Outcome switch
        {
            RequestIdempotencyOutcome.Invalid => Results.BadRequest(new { message = execution.Message }),
            RequestIdempotencyOutcome.InProgress => Results.Conflict(new { message = execution.Message }),
            RequestIdempotencyOutcome.Conflict => Results.Conflict(new { message = execution.Message }),
            _ => throw new InvalidOperationException("Resultado de idempotencia inesperado para falha.")
        };
    }
}
