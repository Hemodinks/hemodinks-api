using HemodinksAPI.Application.Features.GruposMedicos.Commands;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class GrupoMedicoEndpointExtensions
{
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
}
