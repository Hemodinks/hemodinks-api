using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Api;

internal static class RequestIdempotencyReplay
{
    public static RequestIdempotencyExecutionResult<TResponse> BuildExistingResult<TResponse>(
        HttpContext httpContext,
        IdempotencyRequest existingRequest,
        string requestHash)
    {
        if (!string.Equals(existingRequest.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return RequestIdempotencyExecutionResult<TResponse>.Conflict(
                "A mesma Idempotency-Key nao pode ser reutilizada com payload diferente.");
        }

        if (!string.Equals(existingRequest.State, IdempotencyRequestStates.Completed, StringComparison.Ordinal))
        {
            return RequestIdempotencyExecutionResult<TResponse>.InProgress(
                "Ja existe uma requisicao com esta Idempotency-Key em processamento.");
        }

        if (string.IsNullOrWhiteSpace(existingRequest.ResponseJson))
        {
            return RequestIdempotencyExecutionResult<TResponse>.Conflict(
                "O registro de idempotencia existente nao possui resposta para replay.");
        }

        var payload = RequestIdempotencySupport.DeserializePayload<TResponse>(existingRequest.ResponseJson);
        if (payload is null)
        {
            return RequestIdempotencyExecutionResult<TResponse>.Conflict(
                "Nao foi possivel reconstruir a resposta do registro de idempotencia.");
        }

        httpContext.Response.Headers[RequestIdempotencyService.IdempotencyStatusHeaderName] = "replayed";

        return RequestIdempotencyExecutionResult<TResponse>.Replayed(
            payload,
            existingRequest.ResourceLocation);
    }
}
