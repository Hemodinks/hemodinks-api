namespace HemodinksAPI.Api;

public sealed record StoredIdempotentResponse<TResponse>(
    TResponse Payload,
    string? ResourceLocation = null);

public enum RequestIdempotencyOutcome
{
    Executed,
    Replayed,
    Conflict,
    InProgress,
    Invalid
}

public sealed class RequestIdempotencyExecutionResult<TResponse>
{
    private RequestIdempotencyExecutionResult(
        RequestIdempotencyOutcome outcome,
        TResponse? payload,
        string? resourceLocation,
        string? message)
    {
        Outcome = outcome;
        Payload = payload;
        ResourceLocation = resourceLocation;
        Message = message;
    }

    public RequestIdempotencyOutcome Outcome { get; }

    public TResponse? Payload { get; }

    public string? ResourceLocation { get; }

    public string? Message { get; }

    public bool IsSuccessful => Outcome is RequestIdempotencyOutcome.Executed or RequestIdempotencyOutcome.Replayed;

    public static RequestIdempotencyExecutionResult<TResponse> Executed(
        TResponse payload,
        string? resourceLocation = null)
    {
        return new RequestIdempotencyExecutionResult<TResponse>(
            RequestIdempotencyOutcome.Executed,
            payload,
            resourceLocation,
            null);
    }

    public static RequestIdempotencyExecutionResult<TResponse> Replayed(
        TResponse payload,
        string? resourceLocation = null)
    {
        return new RequestIdempotencyExecutionResult<TResponse>(
            RequestIdempotencyOutcome.Replayed,
            payload,
            resourceLocation,
            null);
    }

    public static RequestIdempotencyExecutionResult<TResponse> Conflict(string message)
    {
        return new RequestIdempotencyExecutionResult<TResponse>(
            RequestIdempotencyOutcome.Conflict,
            default,
            null,
            message);
    }

    public static RequestIdempotencyExecutionResult<TResponse> InProgress(string message)
    {
        return new RequestIdempotencyExecutionResult<TResponse>(
            RequestIdempotencyOutcome.InProgress,
            default,
            null,
            message);
    }

    public static RequestIdempotencyExecutionResult<TResponse> Invalid(string message)
    {
        return new RequestIdempotencyExecutionResult<TResponse>(
            RequestIdempotencyOutcome.Invalid,
            default,
            null,
            message);
    }
}
