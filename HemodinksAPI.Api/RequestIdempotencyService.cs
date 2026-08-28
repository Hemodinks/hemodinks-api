using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Idempotency;
using HemodinksAPI.Application.Tenancy;

namespace HemodinksAPI.Api;

public sealed class RequestIdempotencyService
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const string IdempotencyStatusHeaderName = "Idempotency-Status";

    private readonly IIdempotencyRequestStore _store;
    private readonly IClinicaContext _clinicaContext;
    private readonly ILogger<RequestIdempotencyService> _logger;

    public RequestIdempotencyService(
        IIdempotencyRequestStore store,
        IClinicaContext clinicaContext,
        ILogger<RequestIdempotencyService> logger)
    {
        _store = store;
        _clinicaContext = clinicaContext;
        _logger = logger;
    }

    public async Task<RequestIdempotencyExecutionResult<TResponse>> ExecuteAsync<TResponse>(
        HttpContext httpContext,
        string operation,
        string scope,
        object requestPayload,
        int successStatusCode,
        Func<CancellationToken, Task<StoredIdempotentResponse<TResponse>>> action,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeaderName, out var headerValues))
        {
            var freshResponse = await action(cancellationToken);
            return RequestIdempotencyExecutionResult<TResponse>.Executed(
                freshResponse.Payload,
                freshResponse.ResourceLocation);
        }

        var key = RequestIdempotencySupport.NormalizeKey(headerValues.ToString());
        var keyValidation = RequestIdempotencySupport.ValidateKey(key);
        if (keyValidation is not null)
        {
            return RequestIdempotencyExecutionResult<TResponse>.Invalid(keyValidation);
        }

        var normalizedScope = RequestIdempotencySupport.NormalizeScope(BuildScopedScope(scope));
        var requestHash = RequestIdempotencySupport.ComputeHash(requestPayload);

        var existingRequest = await FindExistingAsync(operation, normalizedScope, key, cancellationToken);
        if (existingRequest is not null)
        {
            return RequestIdempotencyReplay.BuildExistingResult<TResponse>(
                httpContext,
                existingRequest,
                requestHash);
        }

        var record = new IdempotencyRequest
        {
            ClinicaId = _clinicaContext.GetRequiredClinicaId(),
            Operation = operation,
            Scope = normalizedScope,
            IdempotencyKey = key,
            RequestHash = requestHash,
            State = IdempotencyRequestStates.InProgress,
            CreatedAt = DateTime.UtcNow
        };

        if (!await _store.TryAddAsync(record, cancellationToken))
        {
            existingRequest = await FindExistingAsync(operation, normalizedScope, key, cancellationToken);
            if (existingRequest is not null)
            {
                return RequestIdempotencyReplay.BuildExistingResult<TResponse>(
                    httpContext,
                    existingRequest,
                    requestHash);
            }

            throw;
        }

        try
        {
            var response = await action(cancellationToken);

            RequestIdempotencySupport.MarkAsCompleted(
                record,
                response.Payload,
                response.ResourceLocation,
                successStatusCode);

            await _store.CompleteAsync(record, cancellationToken);

            httpContext.Response.Headers[IdempotencyStatusHeaderName] = "stored";

            return RequestIdempotencyExecutionResult<TResponse>.Executed(
                response.Payload,
                response.ResourceLocation);
        }
        catch
        {
            try
            {
                await _store.RemoveAsync(record, cancellationToken);
            }
            catch (Exception cleanupException)
            {
                _logger.LogWarning(
                    cleanupException,
                    "Falha ao remover registro de idempotencia incompleto para {Operation}/{Scope}",
                    operation,
                    normalizedScope);
            }

            throw;
        }
    }

    public static string ComputeHash(object payload)
    {
        return RequestIdempotencySupport.ComputeHash(payload);
    }

    private Task<IdempotencyRequest?> FindExistingAsync(
        string operation,
        string scope,
        string key,
        CancellationToken cancellationToken)
    {
        return _store.FindAsync(
            _clinicaContext.GetRequiredClinicaId(),
            operation,
            scope,
            key,
            cancellationToken);
    }

    private string BuildScopedScope(string scope)
    {
        var normalizedScope = RequestIdempotencySupport.NormalizeScope(scope);
        return _clinicaContext.ClinicaId.HasValue
            ? $"clinic:{_clinicaContext.ClinicaId.Value}:{normalizedScope}"
            : normalizedScope;
    }
}
