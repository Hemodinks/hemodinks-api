using System.Globalization;
using Microsoft.Data.SqlClient;

namespace HemodinksAPI.Api;

/// <summary>
/// Separates "the database cannot be reached right now" (for example, a serverless
/// resume) from real SQL failures, which must keep surfacing as 500.
/// </summary>
internal static class DatabaseAvailability
{
    private const int MaxExceptionDepth = 8;

    public static bool IsUnavailable(Exception exception)
    {
        var current = exception;
        for (var depth = 0; current is not null && depth < MaxExceptionDepth; depth++)
        {
            // An authorization or not-found outcome keeps its meaning, whatever caused it.
            if (current is UnauthorizedAccessException or KeyNotFoundException)
                return false;
            if (current is SqlException sql && IsUnavailableSqlError(sql.Number, sql.Class))
                return true;
            current = current.InnerException;
        }
        return false;
    }

    internal static bool IsUnavailableSqlError(int number, byte errorClass) =>
        HemodinksAPI.Infrastructure.Data.SqlAvailabilityErrors.IsUnavailable(number, errorClass);
}

/// <summary>Fixed response: never includes the exception, server or database name.</summary>
internal sealed class DatabaseUnavailableResult : IResult
{
    private const int RetryAfterSeconds = 5;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return Results.Json(new
        {
            code = "database_unavailable",
            requestId = httpContext.TraceIdentifier,
            retryAfterSeconds = RetryAfterSeconds,
            message = "O sistema esta temporariamente indisponivel. Aguarde alguns segundos e tente novamente."
        }, statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(httpContext);
    }
}
