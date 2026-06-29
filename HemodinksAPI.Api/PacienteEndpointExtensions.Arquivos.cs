using System.Security.Claims;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class PacienteEndpointExtensions
{
    private static Task<IResult> UploadArquivo(
        int id,
        IFormFile file,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new UploadPacienteArquivoCommand
            {
                PacienteId = id,
                File = file,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.Created($"/api/pacientes/{id}/arquivos/{result.Id}", result);
        }, logger, "Erro ao enviar arquivo do paciente", "Erro ao enviar arquivo");
    }

    private static Task<IResult> DeleteArquivo(
        int id,
        int arquivoId,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            await mediator.Send(new DeletePacienteArquivoCommand
            {
                PacienteId = id,
                ArquivoId = arquivoId,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.NoContent();
        }, logger, "Erro ao excluir arquivo do paciente", "Erro ao excluir arquivo");
    }
}
