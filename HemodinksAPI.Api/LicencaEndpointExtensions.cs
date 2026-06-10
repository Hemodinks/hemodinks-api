using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
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

    private static async Task<IResult> GetCurrentLicenca(
        ClaimsPrincipal claimsPrincipal,
        ILicencaService licencaService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var licenca = await licencaService.GetCurrentAsync(currentUser, cancellationToken);
            return Results.Ok(licenca);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao consultar licenca atual");
            return Results.BadRequest(new { message = "Erro ao consultar licenca", error = ex.Message });
        }
    }

    private static async Task<IResult> GetUserLicenca(
        int userId,
        ILicencaService licencaService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await licencaService.GetOrCreateForMedicoAsync(userId, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao consultar licenca do usuario {UserId}", userId);
            return Results.BadRequest(new { message = "Erro ao consultar licenca", error = ex.Message });
        }
    }

    private static async Task<IResult> UpdateUserLicenca(
        int userId,
        UpdateLicencaRequest request,
        ILicencaService licencaService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await licencaService.UpdateAsync(userId, request, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao atualizar licenca do usuario {UserId}", userId);
            return Results.BadRequest(new { message = "Erro ao atualizar licenca", error = ex.Message });
        }
    }

    private static async Task<IResult> LiberarLicencaCompleta(
        int userId,
        LiberarLicencaCompletaRequest request,
        ILicencaService licencaService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await licencaService.LiberarCompletaAsync(userId, request, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao liberar licenca completa do usuario {UserId}", userId);
            return Results.BadRequest(new { message = "Erro ao liberar licenca completa", error = ex.Message });
        }
    }
}
