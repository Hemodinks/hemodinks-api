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

    private static Task<IResult> GetHospitais(
        IMediator mediator,
        ILogger<Program> logger)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            return Results.Ok(await mediator.Send(new GetHospitaisQuery()));
        }, logger, "Erro ao buscar hospitais", "Erro ao buscar hospitais");
    }
}
