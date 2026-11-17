using HemodinksAPI.Domain.Exceptions;
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
                LogSanitizer.Clean(context.Request.Method),
                LogSanitizer.Clean(context.Request.Path.Value),
                context.TraceIdentifier);

            context.Response.Clear();
            var result = ApiExceptionResults.Map(
                exception,
                new EndpointErrorOptions
                {
                    NotFoundUsesExceptionMessage = true
                });
            await result.ExecuteAsync(context);
        }
    }
}

internal static class ApiExceptionResults
{
    private const string InvalidPayloadMessage =
        "Alguns campos estao ausentes ou possuem formato invalido. Revise os dados informados.";

    internal const string ConcurrencyMessage =
        "O registro foi alterado por outra operacao. Atualize os dados e tente novamente.";

    // #293: so excecoes de negocio explicitas sao "esperadas" (Warning) e expoem mensagem ao cliente.
    // InvalidOperationException/KeyNotFoundException genericas (EF, LINQ, DI, Dictionary, runtime) sao bug: Error.
    public static bool IsExpected(Exception exception) =>
        exception is HemodinksAPI.Application.Security.PasswordPolicyUnavailableException
            or HemodinksAPI.Application.Security.SensitiveIdentityException
            or HemodinksAPI.Application.Security.CompromisedPasswordException
            or BadHttpRequestException
            or ResourceNotFoundException
            or UnauthorizedAccessException
            or BusinessRuleException
            or DbUpdateConcurrencyException;

    public static IResult Map(Exception exception, EndpointErrorOptions? options = null)
    {
        options ??= EndpointErrorOptions.Default;

        if (DatabaseAvailability.IsUnavailable(exception))
            return new DatabaseUnavailableResult();

        return exception switch
        {
            HemodinksAPI.Application.Security.SensitiveIdentityException identity => Results.Json(
                new { code = identity.Code, message = identity.Message }, statusCode: identity.Code switch
                {
                    "email_confirmation_unavailable" => StatusCodes.Status503ServiceUnavailable,
                    "individual_identity_required" or "identity_revalidation_failed" => StatusCodes.Status403Forbidden,
                    "identity_change_conflict" or "email_change_unavailable" or "email_confirmation_required"
                        or "email_confirmation_attempts_exceeded" => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest
                }),
            HemodinksAPI.Application.Security.CompromisedPasswordException compromised => Results.BadRequest(
                new { code = HemodinksAPI.Application.Security.CompromisedPasswordException.Code, message = compromised.Message }),
            HemodinksAPI.Application.Security.PasswordPolicyUnavailableException unavailable => Results.Json(
                new { code = "password_policy_unavailable", message = unavailable.Message }, statusCode: StatusCodes.Status503ServiceUnavailable),
            BadHttpRequestException => Results.BadRequest(new { message = InvalidPayloadMessage }),
            KeyNotFoundException notFound => MapNotFound(notFound, options),
            UnauthorizedAccessException when options.UnauthorizedAccessAsUnauthorized => Results.Unauthorized(),
            UnauthorizedAccessException => Results.Forbid(),
            DbUpdateConcurrencyException => Results.Conflict(new { message = ConcurrencyMessage }),
            BusinessRuleException businessRule => Results.BadRequest(new { message = businessRule.Message }),
            _ => Results.Problem(
                title: options.InternalServerErrorTitle,
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static IResult MapNotFound(KeyNotFoundException exception, EndpointErrorOptions options)
    {
        // Mensagem da excecao so quando e de negocio; KeyNotFoundException generica traz a chave do Dictionary.
        var message = !options.NotFoundUsesExceptionMessage
            ? options.NotFoundMessage
            : exception is ResourceNotFoundException
                ? exception.Message
                : ResourceNotFoundException.DefaultMessage;
        return message == null
            ? Results.NotFound()
            : Results.NotFound(new { message });
    }
}

