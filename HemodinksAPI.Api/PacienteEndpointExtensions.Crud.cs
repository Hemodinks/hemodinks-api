using System.Security.Claims;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class PacienteEndpointExtensions
{
    private static Task<IResult> GetAllPacientes(
        int? page,
        int? pageSize,
        string? search,
        string? medico,
        string? convenio,
        string? procedimento,
        string? sortBy,
        string? sortDirection,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new GetAllPacientesQuery
            {
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(10),
                Search = search,
                Medico = medico,
                Convenio = convenio,
                Procedimento = procedimento,
                SortBy = sortBy,
                SortDirection = sortDirection,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId,
                CurrentEquipeId = currentUser.EquipeId
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar pacientes", "Erro ao buscar pacientes");
    }

    private static Task<IResult> GetPacienteById(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(
                new GetPacienteByIdQuery(id, currentUser.Id, currentUser.PerfilId, currentUser.EquipeId),
                cancellationToken);

            return result == null ? Results.NotFound() : Results.Ok(result);
        }, logger, "Erro ao buscar paciente", "Erro ao buscar paciente");
    }

    private static Task<IResult> CreatePaciente(
        CreatePacienteCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            ApplyCurrentUser(command, GetRequiredCurrentUser(claimsPrincipal));
            var result = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/pacientes/{result.Id}", result);
        }, logger, "Erro ao criar paciente", "Erro ao criar paciente");
    }

    private static Task<IResult> UpdatePaciente(
        int id,
        UpdatePacienteCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.Id = id;
            ApplyCurrentUser(command, GetRequiredCurrentUser(claimsPrincipal));
            return Results.Ok(await mediator.Send(command, cancellationToken));
        }, logger, "Erro ao atualizar paciente", "Erro ao atualizar paciente");
    }

    private static Task<IResult> DeletePaciente(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            await mediator.Send(new DeletePacienteCommand
            {
                Id = id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.NoContent();
        }, logger, "Erro ao excluir paciente", "Erro ao excluir paciente");
    }
}
