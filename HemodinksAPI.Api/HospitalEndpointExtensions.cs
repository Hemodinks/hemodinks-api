using HemodinksAPI.Application.Features.Hospitais.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static class HospitalEndpointExtensions
{
    public static void MapHospitalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hospitais")
            .WithTags("Hospitais")
            .RequireAuthorization();

        group.MapGet("/", GetHospitais)
            .WithName("GetHospitais")
            .WithSummary("Listar hospitais");
    }

    private static async Task<IResult> GetHospitais(
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            return Results.Ok(await mediator.Send(new GetHospitaisQuery()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar hospitais");
            return Results.Problem(
                title: "Erro ao buscar hospitais",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
