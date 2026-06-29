using System.Security.Claims;
using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class GrupoMedicoEndpointExtensions
{
    private static Task<IResult> GetAll(
        int? page,
        int? pageSize,
        string? search,
        string? sortBy,
        string? sortDirection,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(new GetAllGruposMedicosQuery
            {
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(10),
                Search = search,
                SortBy = sortBy,
                SortDirection = sortDirection
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar grupos medicos", "Erro ao buscar grupos medicos");
    }

    private static Task<IResult> GetScopedMedicalUsers(
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new GetScopedMedicalUsersQuery
            {
                CurrentPerfilId = currentUser.PerfilId,
                CurrentUserId = currentUser.Id
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar medicos disponiveis", "Erro ao buscar medicos disponiveis");
    }

    private static Task<IResult> GetById(
        int id,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(new GetGrupoMedicoByIdQuery(id), cancellationToken);
            return result == null ? Results.NotFound() : Results.Ok(result);
        }, logger, "Erro ao buscar grupo medico", "Erro ao buscar grupo medico");
    }
}
