using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

internal sealed class ApiExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ApiExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            logger.Log(
                ApiExceptionResults.IsExpected(exception) ? LogLevel.Warning : LogLevel.Error,
                exception,
                "Erro ao processar {Method} {Path} [request_id: {RequestId}]",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);

            context.Response.Clear();
            var result = ApiExceptionResults.Map(
                exception,
                new EndpointErrorOptions
                {
                    NotFoundUsesExceptionMessage = true,
                    ConcurrencyUsesExceptionMessage = true
                });
            await result.ExecuteAsync(context);
        }
    }
}

internal static class ApiExceptionResults
{
    private const string InvalidPayloadMessage =
        "Alguns campos estao ausentes ou possuem formato invalido. Revise os dados informados.";

    public static bool IsExpected(Exception exception) =>
        exception is BadHttpRequestException
            or KeyNotFoundException
            or UnauthorizedAccessException
            or InvalidOperationException
            or DbUpdateConcurrencyException;

    public static IResult Map(Exception exception, EndpointErrorOptions? options = null)
    {
        options ??= EndpointErrorOptions.Default;

        return exception switch
        {
            BadHttpRequestException => Results.BadRequest(new { message = InvalidPayloadMessage }),
            KeyNotFoundException notFound => MapNotFound(notFound, options),
            UnauthorizedAccessException when options.UnauthorizedAccessAsUnauthorized => Results.Unauthorized(),
            UnauthorizedAccessException => Results.Forbid(),
            DbUpdateConcurrencyException concurrency => MapConcurrency(concurrency, options),
            InvalidOperationException invalidOperation => Results.BadRequest(new { message = invalidOperation.Message }),
            _ => Results.Problem(
                title: options.InternalServerErrorTitle,
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static IResult MapNotFound(KeyNotFoundException exception, EndpointErrorOptions options)
    {
        var message = options.NotFoundUsesExceptionMessage
            ? exception.Message
            : options.NotFoundMessage;
        return message == null
            ? Results.NotFound()
            : Results.NotFound(new { message });
    }

    private static IResult MapConcurrency(DbUpdateConcurrencyException exception, EndpointErrorOptions options)
    {
        var message = options.ConcurrencyUsesExceptionMessage
            ? exception.Message
            : "O registro foi alterado por outra operacao. Atualize os dados e tente novamente.";
        return Results.Conflict(new { message });
    }
}

