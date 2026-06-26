using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public sealed class RequestIdempotencyService
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const string IdempotencyStatusHeaderName = "Idempotency-Status";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _context;
    private readonly ILogger<RequestIdempotencyService> _logger;

    public RequestIdempotencyService(
        AppDbContext context,
        ILogger<RequestIdempotencyService> logger)
    {
        _context = context;
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
            return RequestIdempotencyExecutionResult<TResponse>.Executed(freshResponse.Payload, freshResponse.ResourceLocation);
        }

        var key = NormalizeKey(headerValues.ToString());
        if (key.Length == 0)
        {
            return RequestIdempotencyExecutionResult<TResponse>.Invalid(
                "O header Idempotency-Key nao pode ser vazio.");
        }

        if (key.Length > 200)
        {
            return RequestIdempotencyExecutionResult<TResponse>.Invalid(
                "O header Idempotency-Key deve ter no maximo 200 caracteres.");
        }

        scope = NormalizeScope(scope);
        var requestHash = ComputeHash(requestPayload);

        var existingRequest = await FindExistingAsync(operation, scope, key, cancellationToken);
        if (existingRequest is not null)
        {
            return BuildExistingResult<TResponse>(httpContext, existingRequest, requestHash);
        }

        var record = new IdempotencyRequest
        {
            Operation = operation,
            Scope = scope,
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
            _logger.LogDebug(ex, "Conflito ao registrar idempotencia para {Operation}/{Scope}", operation, scope);

            existingRequest = await FindExistingAsync(operation, scope, key, cancellationToken);
            if (existingRequest is not null)
            {
                _context.Entry(record).State = EntityState.Detached;
                return BuildExistingResult<TResponse>(httpContext, existingRequest, requestHash);
            }

            throw;
        }

        try
        {
            var response = await action(cancellationToken);

            record.State = IdempotencyRequestStates.Completed;
            record.StatusCode = successStatusCode;
            record.ResourceLocation = response.ResourceLocation;
            record.ResponseJson = JsonSerializer.Serialize(response.Payload, SerializerOptions);
            record.CompletedAt = DateTime.UtcNow;

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
                    scope);
            }

            throw;
        }
    }

    public static string ComputeHash(object payload)
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static RequestIdempotencyExecutionResult<TResponse> BuildExistingResult<TResponse>(
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

        var payload = JsonSerializer.Deserialize<TResponse>(existingRequest.ResponseJson, SerializerOptions);
        if (payload is null)
        {
            return RequestIdempotencyExecutionResult<TResponse>.Conflict(
                "Nao foi possivel reconstruir a resposta do registro de idempotencia.");
        }

        httpContext.Response.Headers[IdempotencyStatusHeaderName] = "replayed";

        return RequestIdempotencyExecutionResult<TResponse>.Replayed(
            payload,
            existingRequest.ResourceLocation);
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

    private static string NormalizeKey(string? key)
    {
        return key?.Trim() ?? string.Empty;
    }

    private static string NormalizeScope(string? scope)
    {
        return string.IsNullOrWhiteSpace(scope)
            ? "anonymous"
            : scope.Trim();
    }
}

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
