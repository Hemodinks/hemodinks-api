using System.Security.Claims;
using HemodinksAPI.Application.Features.Users.Commands;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class UserEndpointExtensions
{
    private static Task<IResult> ChangePassword(
        int id,
        ChangePasswordCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.UserId = id;
            command.CurrentUser = GetRequiredCurrentUser(claimsPrincipal);

            var result = await mediator.Send(command, cancellationToken);
            return Results.Ok(result);
        }, logger, "Erro ao alterar senha", "Erro ao alterar senha", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }

    private static Task<IResult> ResetPassword(
        int id,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(new ResetUserPasswordCommand { UserId = id }, cancellationToken);
            return Results.Ok(result);
        }, logger, "Erro ao resetar senha", "Erro ao resetar senha");
    }

    private static Task<IResult> ResetPasswordByEmail(
        ResetUserPasswordByEmailCommand command,
        HttpContext httpContext,
        RequestIdempotencyService requestIdempotencyService,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.RequestIp = httpContext.Connection.RemoteIpAddress?.ToString();
            var normalizedEmailScope = command.Email.Trim().ToUpperInvariant();

            var execution = await requestIdempotencyService.ExecuteAsync(
                httpContext,
                operation: "users.password-reset.request",
                scope: normalizedEmailScope,
                requestPayload: new
                {
                    Email = normalizedEmailScope
                },
                successStatusCode: StatusCodes.Status200OK,
                action: async ct =>
                {
                    var result = await mediator.Send(command, ct);
                    return new StoredIdempotentResponse<RequestPasswordResetResponse>(result);
                },
                cancellationToken);

            if (!execution.IsSuccessful)
            {
                return RequestIdempotencyHttpResults.ToFailureResult(execution);
            }

            return Results.Ok(execution.Payload);
        }, logger, "Erro ao solicitar reset de senha por email", "Erro ao solicitar reset de senha");
    }

    private static Task<IResult> ConfirmPasswordReset(
        ConfirmPasswordResetCommand command,
        HttpContext httpContext,
        RequestIdempotencyService requestIdempotencyService,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var execution = await requestIdempotencyService.ExecuteAsync(
                httpContext,
                operation: "users.password-reset.confirm",
                scope: RequestIdempotencyService.ComputeHash(new { command.Token }),
                requestPayload: new
                {
                    command.Token,
                    command.NovaSenha
                },
                successStatusCode: StatusCodes.Status200OK,
                action: async ct =>
                {
                    var result = await mediator.Send(command, ct);
                    return new StoredIdempotentResponse<ResetUserPasswordResponse>(result);
                },
                cancellationToken);

            if (!execution.IsSuccessful)
            {
                return RequestIdempotencyHttpResults.ToFailureResult(execution);
            }

            return Results.Ok(execution.Payload);
        }, logger, "Erro ao confirmar reset de senha", "Erro ao confirmar reset de senha");
    }
}
