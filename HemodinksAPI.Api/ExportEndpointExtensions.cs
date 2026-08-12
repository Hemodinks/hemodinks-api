using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Exports.Commands;
using MediatR;

namespace HemodinksAPI.Api;

public static class ExportEndpointExtensions
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/exports")
            .WithTags("Exports")
            .RequireAuthorization()
            .RequireAuthorization("EquipeOperacaoSensivel");

        group.MapPost("/", RequestExport)
            .WithName("RequestFileExport")
            .WithSummary("Solicitar exportacao PDF/XLSX")
            .WithDescription("Enfileira uma exportacao de pacientes, faturamentos medicos ou CBHPM para processamento no HemodinksAPI.Workers. O endpoint responde 202 Accepted.");
    }

    private static Task<IResult> RequestExport(
        RequestFileExportCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.CurrentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(command, cancellationToken);
            return Results.Accepted($"/api/exports/{result.JobId}", result);
        }, logger, "Erro ao solicitar exportacao", "Erro ao solicitar exportacao");
    }

    private static CurrentUserContext GetRequiredCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.ToCurrentUserContext()
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
    }
}
