using HemodinksAPI.Application.Features.Convenios.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static class ConvenioEndpointExtensions
{
    public static void MapConvenioEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/convenios")
            .WithTags("Convenios")
            .RequireAuthorization();

        group.MapGet("/", GetConvenios)
            .WithName("GetConvenios")
            .WithSummary("Listar convenios");
    }

    private static async Task<IResult> GetConvenios(
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            return Results.Ok(await mediator.Send(new GetConveniosQuery()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar convenios");
            return Results.BadRequest(new { message = "Erro ao buscar convenios", error = ex.Message });
        }
    }
}
