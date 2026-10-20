using System.Security.Claims;
using HemodinksAPI.Application.Features.Events;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace HemodinksAPI.Api;

public static partial class EventEndpointExtensions
{
    private static Task<IResult> GetEventEmailNotification(int id, bool? copyToCreator, ClaimsPrincipal claimsPrincipal,
        IMediator mediator, ILogger<Program> logger, CancellationToken cancellationToken) =>
        EndpointExecution.RunAsync(async () => Results.Ok(await mediator.Send(
            new GetEventEmailNotificationQuery(id, GetRequiredNonPatientCurrentUser(claimsPrincipal), copyToCreator ?? false), cancellationToken)),
            logger, "Erro ao consultar notificacao por email", "Erro ao consultar notificacao por email",
            new EndpointErrorOptions { UnauthorizedAccessAsUnauthorized = true });

    private static Task<IResult> QueueEventEmailNotification(int id, [FromBody] EventEmailNotificationRequest? request, ClaimsPrincipal claimsPrincipal,
        IMediator mediator, ILogger<Program> logger, CancellationToken cancellationToken) =>
        EndpointExecution.RunAsync(async () => Results.Accepted(value: await mediator.Send(
            new QueueEventEmailNotificationCommand(id, GetRequiredNonPatientCurrentUser(claimsPrincipal), request?.CopyToCreator ?? false), cancellationToken)),
            logger, "Erro ao agendar notificacao por email", "Erro ao agendar notificacao por email",
            new EndpointErrorOptions { UnauthorizedAccessAsUnauthorized = true });
}

