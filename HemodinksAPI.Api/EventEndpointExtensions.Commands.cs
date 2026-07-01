using System.Globalization;
using System.Security.Claims;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Features.Events.Commands;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class EventEndpointExtensions
{
    private static Task<IResult> CreateEvent(
        ClaimsPrincipal claimsPrincipal,
        HttpContext httpContext,
        EventRequest request,
        RequestIdempotencyService requestIdempotencyService,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);

            var execution = await requestIdempotencyService.ExecuteAsync(
                httpContext,
                operation: "events.create",
                scope: currentUser.Id.ToString(CultureInfo.InvariantCulture),
                requestPayload: request,
                successStatusCode: StatusCodes.Status201Created,
                action: async ct =>
                {
                    var result = await mediator.Send(new CreateEventCommand
                    {
                        CurrentUser = currentUser,
                        Request = request
                    }, ct);

                    return new StoredIdempotentResponse<EventDto>(
                        result,
                        $"/api/events/{result.Id}");
                },
                cancellationToken);

            if (!execution.IsSuccessful)
            {
                return RequestIdempotencyHttpResults.ToFailureResult(execution);
            }

            return Results.Created(execution.ResourceLocation!, execution.Payload);
        }, logger, "Erro ao criar evento da agenda", "Erro ao criar evento da agenda", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }

    private static Task<IResult> UpdateEvent(
        int id,
        ClaimsPrincipal claimsPrincipal,
        EventRequest request,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            return Results.Ok(await mediator.Send(new UpdateEventCommand
            {
                Id = id,
                CurrentUser = currentUser,
                Request = request
            }, cancellationToken));
        }, logger, "Erro ao atualizar evento da agenda", "Erro ao atualizar evento da agenda", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }

    private static Task<IResult> CompleteEvent(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            await mediator.Send(new CompleteEventCommand
            {
                Id = id,
                CurrentUser = currentUser
            }, cancellationToken);

            return Results.NoContent();
        }, logger, "Erro ao concluir evento da agenda", "Erro ao concluir evento da agenda", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }

    private static Task<IResult> DeleteEvent(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            await mediator.Send(new DeleteEventCommand
            {
                Id = id,
                CurrentUser = currentUser
            }, cancellationToken);

            return Results.NoContent();
        }, logger, "Erro ao excluir evento da agenda", "Erro ao excluir evento da agenda", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }
}
