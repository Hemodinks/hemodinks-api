using HemodinksAPI.Application.Features.ConfiguracoesSistema.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static class ConfiguracaoSistemaEndpointExtensions
{
    public static void MapConfiguracaoSistemaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/configuracoes-sistema")
            .WithTags("ConfiguracoesSistema")
            .RequireAuthorization();

        group.MapGet("/current", GetCurrent)
            .WithName("GetConfiguracaoSistema")
            .WithSummary("Consultar configuracao do sistema")
            .AllowAnonymous();

        group.MapGet("/current/foto-empresa", GetCurrentPhoto)
            .WithName("GetConfiguracaoSistemaPhoto")
            .WithSummary("Consultar foto da empresa")
            .AllowAnonymous();

    }

    private static Task<IResult> GetCurrent(
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(new GetConfiguracaoSistemaQuery(), cancellationToken);
            return Results.Ok(result);
        }, logger, "Erro ao consultar configuracao do sistema", "Erro ao consultar configuracao do sistema");
    }

    private static Task<IResult> GetCurrentPhoto(
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var photo = await mediator.Send(new GetConfiguracaoSistemaPhotoQuery(), cancellationToken);
            return photo == null
                ? Results.NotFound()
                : Results.Stream(photo.Content, photo.ContentType);
        }, logger, "Erro ao consultar foto da empresa", "Erro ao consultar foto da empresa");
    }

}
