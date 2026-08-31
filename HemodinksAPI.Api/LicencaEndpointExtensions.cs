using System.Security.Claims;
using HemodinksAPI.Application.Features.Licencas;

namespace HemodinksAPI.Api;

public static class LicencaEndpointExtensions
{
    public static void MapLicencaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/licencas")
            .WithTags("Licencas")
            .RequireAuthorization();

        group.MapGet("/current", GetCurrentLicenca)
            .WithName("GetCurrentLicenca")
            .WithSummary("Consultar licenca do usuario autenticado");

        group.MapGet("/users/{userId}", GetUserLicenca)
            .WithName("GetUserLicenca")
            .WithSummary("Consultar licenca de um medico")
            .RequireAuthorization("Administrador");

        group.MapPut("/users/{userId}", UpdateUserLicenca)
            .WithName("UpdateUserLicenca")
            .WithSummary("Atualizar licenca de um medico")
            .RequireAuthorization("Administrador");

        group.MapPost("/users/{userId}/liberar-completa", LiberarLicencaCompleta)
            .WithName("LiberarLicencaCompleta")
            .WithSummary("Liberar plano completo para um medico")
            .RequireAuthorization("Administrador");
    }

    private static Task<IResult> GetCurrentLicenca(
        ClaimsPrincipal claimsPrincipal,
        ILicencaService licencaService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var licenca = await licencaService.GetCurrentAsync(currentUser, cancellationToken);
            return Results.Ok(licenca);
        }, logger, "Erro ao consultar licenca atual", "Erro ao consultar licenca");
    }

    private static Task<IResult> GetUserLicenca(
        int userId,
        ILicencaService licencaService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            return Results.Ok(await licencaService.GetOrCreateForMedicoAsync(userId, cancellationToken));
        }, logger, "Erro ao consultar licenca do usuario {UserId}", "Erro ao consultar licenca");
    }

    private static Task<IResult> UpdateUserLicenca(
        int userId,
        UpdateLicencaRequest request,
        ILicencaService licencaService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            return Results.Ok(await licencaService.UpdateAsync(userId, request, cancellationToken));
        }, logger, "Erro ao atualizar licenca do usuario {UserId}", "Erro ao atualizar licenca");
    }

    private static Task<IResult> LiberarLicencaCompleta(
        int userId,
        LiberarLicencaCompletaRequest request,
        ILicencaService licencaService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            return Results.Ok(await licencaService.LiberarCompletaAsync(userId, request, cancellationToken));
        }, logger, "Erro ao liberar licenca completa do usuario {UserId}", "Erro ao liberar licenca completa");
    }
}
