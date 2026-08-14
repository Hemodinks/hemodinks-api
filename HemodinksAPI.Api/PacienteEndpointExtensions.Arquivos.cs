using System.Security.Claims;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class PacienteEndpointExtensions
{
    private static Task<IResult> DownloadArquivo(
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
            var file = await mediator.Send(new DownloadPacienteArquivoQuery(
                id,
                arquivoId,
                currentUser.Id,
                currentUser.PerfilId,
                currentUser.EquipeId), cancellationToken);

            return file == null
                ? Results.NotFound()
                : Results.Stream(
                    file.Content,
                    file.ContentType,
                    fileDownloadName: file.FileName,
                    enableRangeProcessing: true);
        }, logger, "Erro ao baixar arquivo do paciente", "Erro ao baixar arquivo");
    }

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
                File = file.ToUploadedFile(),
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId,
                CurrentEquipeId = currentUser.EquipeId
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
                CurrentPerfilId = currentUser.PerfilId,
                CurrentEquipeId = currentUser.EquipeId
            }, cancellationToken);

            return Results.NoContent();
        }, logger, "Erro ao excluir arquivo do paciente", "Erro ao excluir arquivo");
    }
}
