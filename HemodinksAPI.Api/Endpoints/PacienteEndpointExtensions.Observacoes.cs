using System.Security.Claims;
using HemodinksAPI.Application.Features.Pacientes.Observacoes;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class PacienteEndpointExtensions
{
    private static Task<IResult> GetObservacoes(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new GetPacienteObservacoesQuery
            {
                PacienteId = id,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar observacoes do paciente", "Erro ao buscar observacoes do paciente");
    }

    private static Task<IResult> CreateObservacao(
        int id,
        CreatePacienteObservacaoCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.PacienteId = id;
            ApplyCurrentUser(command, GetRequiredCurrentUser(claimsPrincipal));
            return Results.Ok(await mediator.Send(command, cancellationToken));
        }, logger, "Erro ao registrar observacao do paciente", "Erro ao registrar observacao do paciente");
    }

    private static Task<IResult> MarkObservacoesAsRead(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new MarkPacienteObservacoesAsReadCommand
            {
                PacienteId = id,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao marcar observacoes do paciente como lidas", "Erro ao atualizar observacoes do paciente");
    }
}
