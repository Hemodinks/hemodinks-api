using System.Security.Claims;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Features.Events.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class EventEndpointExtensions
{
    private static Task<IResult> GetMedicalUsers(
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredNonPatientCurrentUser(claimsPrincipal);
            return Results.Ok(await mediator.Send(new GetEventMedicalUsersQuery { CurrentUser = currentUser }, cancellationToken));
        }, logger, "Erro ao buscar medicos para agenda", "Erro ao buscar medicos para agenda");
    }

    private static Task<IResult> GetNotificationRecipients(
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredNonPatientCurrentUser(claimsPrincipal);
            return Results.Ok(await mediator.Send(new GetAgendaNotificationRecipientOptionsQuery
            {
                CurrentUser = currentUser
            }, cancellationToken));
        }, logger, "Erro ao buscar destinatarios de notificacoes da agenda", "Erro ao buscar destinatarios de notificacoes da agenda", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }

    private static Task<IResult> MarkNotificationsRead(
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredNonPatientCurrentUser(claimsPrincipal);
            var updatedCount = await mediator.Send(new MarkAgendaNotificationsAsReadCommand
            {
                CurrentUser = currentUser
            }, cancellationToken);

            return Results.Ok(new { updatedCount });
        }, logger, "Erro ao marcar notificacoes da agenda como lidas", "Erro ao marcar notificacoes da agenda como lidas", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }

    private static Task<IResult> GetEvents(
        DateTime? from,
        DateTime? to,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            return Results.Ok(await mediator.Send(new GetEventsQuery
            {
                From = from,
                To = to,
                CurrentUser = currentUser
            }, cancellationToken));
        }, logger, "Erro ao buscar eventos da agenda", "Erro ao buscar eventos da agenda", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }

    private static Task<IResult> GetEventById(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new GetEventByIdQuery
            {
                Id = id,
                CurrentUser = currentUser
            }, cancellationToken);

            return result == null ? Results.NotFound() : Results.Ok(result);
        }, logger, "Erro ao buscar evento da agenda", "Erro ao buscar evento da agenda", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }
}
