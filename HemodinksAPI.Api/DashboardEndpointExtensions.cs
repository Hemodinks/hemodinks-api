using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Dashboard.Queries;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Services;
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
            .WithSummary("Resumo do dashboard")
            .RequireAuthorization(LicencaPolicies.DashboardVisualizar);

        group.MapGet("/notifications", GetNotifications)
            .WithName("GetDashboardNotifications")
            .WithSummary("Notificacoes do dashboard")
            .RequireAuthorization(LicencaPolicies.DashboardVisualizar);
    }

    private static async Task<IResult> GetSummary(
        ClaimsPrincipal claimsPrincipal,
        IEventReminderProcessor reminderProcessor,
        IMediator mediator,
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

            await ProcessDueRemindersWithoutBlockingDashboardAsync(
                reminderProcessor,
                logger,
                cancellationToken);

            return Results.Ok(await mediator.Send(new GetDashboardSummaryQuery
            {
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar resumo do dashboard");
            return Results.BadRequest(new { message = "Erro ao buscar resumo do dashboard", error = ex.Message });
        }
    }

    private static async Task<IResult> GetNotifications(
        ClaimsPrincipal claimsPrincipal,
        IEventReminderProcessor reminderProcessor,
        IMediator mediator,
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

            await ProcessDueRemindersWithoutBlockingDashboardAsync(
                reminderProcessor,
                logger,
                cancellationToken);

            return Results.Ok(await mediator.Send(new GetDashboardNotificationsQuery
            {
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar notificacoes do dashboard");
            return Results.BadRequest(new { message = "Erro ao buscar notificacoes do dashboard", error = ex.Message });
        }
    }

    private static async Task ProcessDueRemindersWithoutBlockingDashboardAsync(
        IEventReminderProcessor reminderProcessor,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await reminderProcessor.ProcessDueRemindersAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao processar lembretes durante abertura do dashboard");
        }
    }
}
