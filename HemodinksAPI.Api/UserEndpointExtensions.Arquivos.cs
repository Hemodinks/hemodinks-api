using System.Security.Claims;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Features.Users.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class UserEndpointExtensions
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
            var file = await mediator.Send(new DownloadUserArquivoQuery(
                id,
                arquivoId,
                GetRequiredCurrentUser(claimsPrincipal)), cancellationToken);

            return file == null
                ? Results.NotFound()
                : Results.Stream(
                    file.Content,
                    file.ContentType,
                    fileDownloadName: file.FileName,
                    enableRangeProcessing: true);
        }, logger, "Erro ao baixar arquivo do usuario", "Erro ao baixar arquivo");
    }

    private static Task<IResult> GetProfilePhoto(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var photo = await mediator.Send(new GetUserProfilePhotoQuery
            {
                Id = id,
                CurrentUser = GetRequiredCurrentUser(claimsPrincipal)
            }, cancellationToken);

            return photo == null
                ? Results.NotFound()
                : Results.Stream(photo.Content, photo.ContentType);
        }, logger, "Erro ao buscar foto de perfil", "Erro ao buscar foto de perfil");
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
            var result = await mediator.Send(new UploadUserArquivoCommand
            {
                UserId = id,
                File = file,
                CurrentUser = GetRequiredCurrentUser(claimsPrincipal)
            }, cancellationToken);

            return Results.Created($"/api/users/{id}/arquivos/{result.Id}", result);
        }, logger, "Erro ao enviar arquivo do usuario", "Erro ao enviar arquivo");
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
            await mediator.Send(new DeleteUserArquivoCommand
            {
                UserId = id,
                ArquivoId = arquivoId,
                CurrentUser = GetRequiredCurrentUser(claimsPrincipal)
            }, cancellationToken);

            return Results.NoContent();
        }, logger, "Erro ao excluir arquivo do usuario", "Erro ao excluir arquivo");
    }
}
