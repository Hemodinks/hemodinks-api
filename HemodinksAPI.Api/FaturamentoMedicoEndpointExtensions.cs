using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Faturamentos.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static class FaturamentoMedicoEndpointExtensions
{
    public static void MapFaturamentoMedicoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/faturamentos-medicos")
            .WithTags("Faturamento medico")
            .RequireAuthorization();

        group.MapGet("/", GetAllFaturamentosMedicos)
            .WithName("GetAllFaturamentosMedicos")
            .WithSummary("Listar faturamentos medicos")
            .RequireAuthorization("FaturamentoMedicoVisualizar");
    }

    private static Task<IResult> GetAllFaturamentosMedicos(
        int? page,
        int? pageSize,
        string? search,
        string? medico,
        string? convenio,
        string? procedimento,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new GetAllFaturamentosMedicosQuery
            {
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(100),
                Search = search,
                Medico = medico,
                Convenio = convenio,
                Procedimento = procedimento,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar faturamentos medicos", "Erro ao buscar faturamentos medicos");
    }

    private static CurrentUserContext GetRequiredCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.ToCurrentUserContext()
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
    }
}
