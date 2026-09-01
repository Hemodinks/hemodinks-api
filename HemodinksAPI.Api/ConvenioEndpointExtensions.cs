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

    private static Task<IResult> GetConvenios(
        IMediator mediator,
        ILogger<Program> logger)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            return Results.Ok(await mediator.Send(new GetConveniosQuery()));
        }, logger, "Erro ao buscar convenios", "Erro ao buscar convenios");
    }
}
