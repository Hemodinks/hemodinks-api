using System.Globalization;
using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Events;
using HemodinksAPI.Application.Features.Events.Commands;
using HemodinksAPI.Application.Features.Events.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static class EventEndpointExtensions
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/events")
            .WithTags("Agenda e notificacoes")
            .RequireAuthorization();

        group.MapGet("/", GetEvents)
            .WithName("GetEvents")
            .WithSummary("Listar eventos da agenda");

        group.MapGet("/medical-users", GetMedicalUsers)
            .WithName("GetEventMedicalUsers")
            .WithSummary("Listar medicos ativos para notificacao de eventos");

        group.MapGet("/notification-recipients", GetNotificationRecipients)
            .WithName("GetEventNotificationRecipients")
            .WithSummary("Listar destinatarios permitidos para notificacoes da agenda");

        group.MapPost("/notifications/mark-read", MarkNotificationsRead)
            .WithName("MarkAgendaNotificationsRead")
            .WithSummary("Marcar notificacoes da agenda como lidas");

        group.MapGet("/{id:int}", GetEventById)
            .WithName("GetEventById")
            .WithSummary("Buscar evento da agenda por ID");

        group.MapPost("/", CreateEvent)
            .WithName("CreateEvent")
            .WithSummary("Criar evento na agenda")
            .WithDescription("Cria evento na agenda. Envie Idempotency-Key para tornar retries seguros.");

        group.MapPut("/{id:int}", UpdateEvent)
            .WithName("UpdateEvent")
            .WithSummary("Atualizar evento da agenda");

        group.MapPost("/{id:int}/complete", CompleteEvent)
            .WithName("CompleteEvent")
            .WithSummary("Marcar evento como concluido");

        group.MapDelete("/{id:int}", DeleteEvent)
            .WithName("DeleteEvent")
            .WithSummary("Excluir evento da agenda");
    }

    private static async Task<IResult> GetMedicalUsers(
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await mediator.Send(new GetEventMedicalUsersQuery(), cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar medicos para agenda");
            return Results.Problem(
                title: "Erro ao buscar medicos para agenda",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetNotificationRecipients(
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null || currentUser.IsPaciente)
            {
                return Results.Forbid();
            }

            return Results.Ok(await mediator.Send(new GetAgendaNotificationRecipientOptionsQuery
            {
                CurrentUser = currentUser
            }, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar destinatarios de notificacoes da agenda");
            return Results.Problem(
                title: "Erro ao buscar destinatarios de notificacoes da agenda",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> MarkNotificationsRead(
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null || currentUser.IsPaciente)
            {
                return Results.Forbid();
            }

            var updatedCount = await mediator.Send(new MarkAgendaNotificationsAsReadCommand
            {
                CurrentUser = currentUser
            }, cancellationToken);

            return Results.Ok(new { updatedCount });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao marcar notificacoes da agenda como lidas");
            return Results.Problem(
                title: "Erro ao marcar notificacoes da agenda como lidas",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetEvents(
        DateTime? from,
        DateTime? to,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            return Results.Ok(await mediator.Send(new GetEventsQuery
            {
                From = from,
                To = to,
                CurrentUser = currentUser
            }, cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar eventos da agenda");
            return Results.Problem(
                title: "Erro ao buscar eventos da agenda",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetEventById(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var result = await mediator.Send(new GetEventByIdQuery
            {
                Id = id,
                CurrentUser = currentUser
            }, cancellationToken);

            return result == null ? Results.NotFound() : Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar evento da agenda {EventId}", id);
            return Results.Problem(
                title: "Erro ao buscar evento da agenda",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> CreateEvent(
        ClaimsPrincipal claimsPrincipal,
        HttpContext httpContext,
        EventRequest request,
        RequestIdempotencyService requestIdempotencyService,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

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
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao criar evento da agenda");
            return Results.Problem(
                title: "Erro ao criar evento da agenda",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> UpdateEvent(
        int id,
        ClaimsPrincipal claimsPrincipal,
        EventRequest request,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            return Results.Ok(await mediator.Send(new UpdateEventCommand
            {
                Id = id,
                CurrentUser = currentUser,
                Request = request
            }, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao atualizar evento da agenda {EventId}", id);
            return Results.Problem(
                title: "Erro ao atualizar evento da agenda",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> CompleteEvent(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            await mediator.Send(new CompleteEventCommand
            {
                Id = id,
                CurrentUser = currentUser
            }, cancellationToken);

            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao concluir evento da agenda {EventId}", id);
            return Results.Problem(
                title: "Erro ao concluir evento da agenda",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DeleteEvent(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            await mediator.Send(new DeleteEventCommand
            {
                Id = id,
                CurrentUser = currentUser
            }, cancellationToken);

            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao excluir evento da agenda {EventId}", id);
            return Results.Problem(
                title: "Erro ao excluir evento da agenda",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
