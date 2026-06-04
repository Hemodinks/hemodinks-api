using HemodinksAPI.Api.Features.Cbhpm.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static class CbhpmEndpointExtensions
{
    public static void MapCbhpmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/cbhpm")
            .WithTags("CBHPM")
            .RequireAuthorization();

        group.MapGet("/", GetCbhpmGeral)
            .WithName("GetCbhpmGeral")
            .WithSummary("Listar procedimentos CBHPM");
    }

    private static async Task<IResult> GetCbhpmGeral(
        int? page,
        int? pageSize,
        string? search,
        string? codigo,
        string? procedimento,
        string? porte,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            return Results.Ok(await mediator.Send(new GetCbhpmGeralQuery
            {
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(10),
                Search = search,
                Codigo = codigo,
                Procedimento = procedimento,
                Porte = porte
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar procedimentos CBHPM");
            return Results.BadRequest(new { message = "Erro ao buscar procedimentos CBHPM", error = ex.Message });
        }
    }
}
