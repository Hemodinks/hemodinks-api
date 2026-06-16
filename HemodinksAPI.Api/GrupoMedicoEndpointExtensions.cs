using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.GruposMedicos.Commands;
using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static class GrupoMedicoEndpointExtensions
{
    public static void MapGrupoMedicoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/grupos-medicos")
            .WithTags("GruposMedicos")
            .RequireAuthorization();

        group.MapGet("/", GetAll)
            .WithSummary("Listar grupos medicos")
            .RequireAuthorization("Administrador");

        group.MapGet("/medicos", GetScopedMedicalUsers)
            .WithSummary("Listar medicos disponiveis conforme o escopo do usuario");

        group.MapGet("/{id}", GetById)
            .WithSummary("Buscar grupo medico por ID")
            .RequireAuthorization("Administrador");

        group.MapPost("/", Create)
            .WithSummary("Criar grupo medico")
            .RequireAuthorization("Administrador");

        group.MapPut("/{id}", Update)
            .WithSummary("Atualizar grupo medico")
            .RequireAuthorization("Administrador");

        group.MapDelete("/{id}", Delete)
            .WithSummary("Excluir grupo medico")
            .RequireAuthorization("Administrador");
    }

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

    private static Task<IResult> Create(
        CreateGrupoMedicoCommand command,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/grupos-medicos/{result.Id}", result);
        }, logger, "Erro ao criar grupo medico", "Erro ao criar grupo medico");
    }

    private static Task<IResult> Update(
        int id,
        UpdateGrupoMedicoCommand command,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.Id = id;
            return Results.Ok(await mediator.Send(command, cancellationToken));
        }, logger, "Erro ao atualizar grupo medico", "Erro ao atualizar grupo medico");
    }

    private static Task<IResult> Delete(
        int id,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            await mediator.Send(new DeleteGrupoMedicoCommand { Id = id }, cancellationToken);
            return Results.NoContent();
        }, logger, "Erro ao excluir grupo medico", "Erro ao excluir grupo medico");
    }

    private static CurrentUserContext GetRequiredCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.ToCurrentUserContext()
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
    }
}
