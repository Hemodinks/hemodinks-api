using HemodinksAPI.Application.Features.Opme.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static class OpmeEndpointExtensions
{
    public static void MapOpmeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/opme")
            .WithTags("OPME")
            .RequireAuthorization();

        group.MapGet("/", GetOpme)
            .WithName("GetOpme")
            .WithSummary("Listar fornecedores OPME");
    }

    private static async Task<IResult> GetOpme(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await mediator.Send(new GetOpmeQuery(), cancellationToken));
    }
}
