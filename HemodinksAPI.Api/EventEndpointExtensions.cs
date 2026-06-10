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
            .WithTags("Agenda")
            .RequireAuthorization();

        group.MapGet("/", GetEvents)
            .WithName("GetEvents")
            .WithSummary("Listar eventos da agenda");

        group.MapGet("/medical-users", GetMedicalUsers)
            .WithName("GetEventMedicalUsers")
            .WithSummary("Listar medicos ativos para notificacao de eventos");

        group.MapGet("/{id:int}", GetEventById)
            .WithName("GetEventById")
            .WithSummary("Buscar evento da agenda por ID");

        group.MapPost("/", CreateEvent)
            .WithName("CreateEvent")
            .WithSummary("Criar evento na agenda");

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
            return Results.BadRequest(new { message = "Erro ao buscar medicos para agenda", error = ex.Message });
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
            return Results.BadRequest(new { message = "Erro ao buscar eventos da agenda", error = ex.Message });
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
            return Results.BadRequest(new { message = "Erro ao buscar evento da agenda", error = ex.Message });
        }
    }

    private static async Task<IResult> CreateEvent(
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

            var result = await mediator.Send(new CreateEventCommand
            {
                CurrentUser = currentUser,
                Request = request
            }, cancellationToken);

            return Results.Created($"/api/events/{result.Id}", result);
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
            return Results.BadRequest(new { message = "Erro ao criar evento da agenda", error = ex.Message });
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
            return Results.BadRequest(new { message = "Erro ao atualizar evento da agenda", error = ex.Message });
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
            return Results.BadRequest(new { message = "Erro ao concluir evento da agenda", error = ex.Message });
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
            return Results.BadRequest(new { message = "Erro ao excluir evento da agenda", error = ex.Message });
        }
    }
}
