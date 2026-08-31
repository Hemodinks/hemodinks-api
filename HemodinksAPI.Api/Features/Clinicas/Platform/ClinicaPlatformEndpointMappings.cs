using System.Security.Claims;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Api;

public static partial class ClinicaPlatformEndpointExtensions
{
    private static RouteGroupBuilder MapClinicPlatformGroup(WebApplication app)
    {
        return app.MapGroup("/api/platform/clinicas")
            .WithTags("Plataforma - Clinicas")
            .RequireAuthorization("Administrador")
            .AddEndpointFilter(RestrictAdministratorToCurrentClinicAsync);
    }

    private static void MapClinicCrudEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/", ListClinicas);
        group.MapGet("/{id:int}", GetClinica);
        group.MapPost("/", CreateClinica).RequireAuthorization("SuperAdministrador");
        group.MapPut("/{id:int}", UpdateClinica);
        group.MapDelete("/{id:int}", DeactivateClinica).RequireAuthorization("SuperAdministrador");
    }

    private static void MapClinicTeamEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{id:int}/equipes", ListClinicTeams);
        group.MapGet("/{id:int}/equipes/usuarios", ListClinicTeamUsers);
        group.MapPut("/{id:int}/equipes/{teamId:int}", UpdateClinicTeam);
        group.MapPost("/{id:int}/equipes/{teamId:int}/membros", AddClinicTeamMember);
        group.MapDelete("/{id:int}/equipes/{teamId:int}/membros/{userId:int}", RemoveClinicTeamMember);
        group.MapPost("/{id:int}/equipes/{teamId:int}/operadores/{operatorId:int}/pin", ResetClinicTeamOperatorPin);
    }

    private static void MapPlatformAuditEndpoints(WebApplication app)
    {
        app.MapGet("/api/platform/auditoria", ListPlatformAudit)
            .WithTags("Plataforma - Auditoria")
            .RequireAuthorization("SuperAdministrador");
    }

    private static async ValueTask<object?> RestrictAdministratorToCurrentClinicAsync(
        EndpointFilterInvocationContext invocationContext,
        EndpointFilterDelegate next)
    {
        var principal = invocationContext.HttpContext.User;
        if (principal.FindFirstValue("perfilId") == Perfil.AdministradorId.ToString()
            && invocationContext.HttpContext.Request.RouteValues.TryGetValue("id", out var routeId)
            && int.TryParse(routeId?.ToString(), out var requestedClinicId)
            && (!int.TryParse(principal.FindFirstValue(ClinicaClaimTypes.ClinicaId), out var currentClinicId)
                || currentClinicId != requestedClinicId))
        {
            return Results.Forbid();
        }

        return await next(invocationContext);
    }
}

