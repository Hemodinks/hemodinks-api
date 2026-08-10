using System.Globalization;
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
        string? competenciaInicio,
        string? competenciaFinal,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var parsedCompetenciaInicio = ParseCompetencia(competenciaInicio, nameof(competenciaInicio));
            var parsedCompetenciaFinal = ParseCompetencia(competenciaFinal, nameof(competenciaFinal));
            var result = await mediator.Send(new GetAllFaturamentosMedicosQuery
            {
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(100),
                Search = search,
                Medico = medico,
                Convenio = convenio,
                Procedimento = procedimento,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId,
                CurrentEquipeId = currentUser.EquipeId,
                CompetenciaInicio = parsedCompetenciaInicio,
                CompetenciaFinal = parsedCompetenciaFinal
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar faturamentos medicos", "Erro ao buscar faturamentos medicos");
    }

    private static CurrentUserContext GetRequiredCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.ToCurrentUserContext()
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
    }

    private static DateTime? ParseCompetencia(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        string[] acceptedFormats =
        [
            "MM/yyyy",
            "M/yyyy",
            "MM-yyyy",
            "M-yyyy",
            "yyyy-MM",
            "yyyy-M",
            "yyyy-MM-dd",
            "yyyy-M-d"
        ];

        if (DateTime.TryParseExact(
                trimmed,
                acceptedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exactDate))
        {
            return exactDate;
        }

        if (DateTime.TryParse(
                trimmed,
                CultureInfo.GetCultureInfo("pt-BR"),
                DateTimeStyles.None,
                out var ptBrDate))
        {
            return ptBrDate;
        }

        if (DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var invariantDate))
        {
            return invariantDate;
        }

        throw new InvalidOperationException(
            $"Parametro {parameterName} invalido. Use MM/yyyy, yyyy-MM ou yyyy-MM-dd.");
    }
}
