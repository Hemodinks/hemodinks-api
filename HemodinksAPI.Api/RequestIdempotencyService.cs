using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public sealed class RequestIdempotencyService
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const string IdempotencyStatusHeaderName = "Idempotency-Status";

    private readonly AppDbContext _context;
    private readonly IClinicaContext _clinicaContext;
    private readonly ILogger<RequestIdempotencyService> _logger;

    public RequestIdempotencyService(
        AppDbContext context,
        IClinicaContext clinicaContext,
        ILogger<RequestIdempotencyService> logger)
    {
        _context = context;
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

        _context.IdempotencyRequests.Add(record);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogDebug(ex, "Conflito ao registrar idempotencia para {Operation}/{Scope}", operation, normalizedScope);

            existingRequest = await FindExistingAsync(operation, normalizedScope, key, cancellationToken);
            if (existingRequest is not null)
            {
                _context.Entry(record).State = EntityState.Detached;
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

            await _context.SaveChangesAsync(cancellationToken);

            httpContext.Response.Headers[IdempotencyStatusHeaderName] = "stored";

            return RequestIdempotencyExecutionResult<TResponse>.Executed(
                response.Payload,
                response.ResourceLocation);
        }
        catch
        {
            _context.IdempotencyRequests.Remove(record);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
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
        return _context.IdempotencyRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Operation == operation
                    && item.Scope == scope
                    && item.IdempotencyKey == key,
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
