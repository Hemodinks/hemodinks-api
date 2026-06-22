using HemodinksAPI.Application.Features.ConfiguracoesSistema.Commands;
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

        group.MapPut("/current", Update)
            .WithName("UpdateConfiguracaoSistema")
            .WithSummary("Atualizar configuracao do sistema")
            .RequireAuthorization("Administrador");
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

    private static Task<IResult> Update(
        UpdateConfiguracaoSistemaCommand command,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(command, cancellationToken);
            return Results.Ok(result);
        }, logger, "Erro ao atualizar configuracao do sistema", "Erro ao atualizar configuracao do sistema");
    }
}
