using HemodinksAPI.Application.Features.Cbhpm.Commands;
using HemodinksAPI.Application.Features.Cbhpm.Queries;
using HemodinksAPI.Application.Features.Licencas;
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
            .WithSummary("Listar procedimentos CBHPM")
            .RequireAuthorization(LicencaPolicies.CbhpmConsultar);

        group.MapPost("/import", ImportCbhpmGeral)
            .RequireAuthorization("Administrador")
            .WithName("ImportCbhpmGeral")
            .WithSummary("Importar procedimentos CBHPM");
    }

    private static Task<IResult> GetCbhpmGeral(
        int? page,
        int? pageSize,
        string? search,
        string? codigo,
        string? procedimento,
        string? porte,
        string? sortBy,
        string? sortDirection,
        IMediator mediator,
        ILogger<Program> logger)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            return Results.Ok(await mediator.Send(new GetCbhpmGeralQuery
            {
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(10),
                Search = search,
                Codigo = codigo,
                Procedimento = procedimento,
                Porte = porte,
                SortBy = sortBy,
                SortDirection = sortDirection
            }));
        }, logger, "Erro ao buscar procedimentos CBHPM", "Erro ao buscar procedimentos CBHPM");
    }

    private static Task<IResult> ImportCbhpmGeral(
        ImportCbhpmGeralCommand command,
        IMediator mediator,
        ILogger<Program> logger)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            return Results.Ok(await mediator.Send(command));
        }, logger, "Erro ao importar procedimentos CBHPM", "Erro ao importar procedimentos CBHPM");
    }
}
