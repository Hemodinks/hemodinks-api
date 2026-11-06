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

    // Class 20 is how SqlClient reports connection-level failures, whose number varies by platform.
    internal static bool IsUnavailableSqlError(int number, byte errorClass) => errorClass == 20 || number is
        -2 // Timeout.
        or 64 or 233 or 10053 or 10054 or 10060 or 10061 or 11001 // Transport.
        or 40197 or 40501 or 40613 or 49918 or 49919 or 49920 // Azure SQL unavailable or busy.
        or 10928 or 10929; // Azure SQL resource limits.
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
