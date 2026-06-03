using System.Security.Claims;
using HemodinksAPI.Api.Authorization;
using HemodinksAPI.Api.Features.Dashboard.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static class DashboardEndpointExtensions
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization();

        group.MapGet("/summary", GetSummary)
            .WithName("GetDashboardSummary")
            .WithSummary("Resumo do dashboard");
    }

    private static async Task<IResult> GetSummary(
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            return Results.Ok(await mediator.Send(new GetDashboardSummaryQuery
            {
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId,
                CurrentUserName = currentUser.Nome
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar resumo do dashboard");
            return Results.BadRequest(new { message = "Erro ao buscar resumo do dashboard", error = ex.Message });
        }
    }
}
