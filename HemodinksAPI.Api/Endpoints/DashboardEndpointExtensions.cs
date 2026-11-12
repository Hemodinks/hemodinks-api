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

    private static Task<IResult> GetSummary(
        ClaimsPrincipal claimsPrincipal,
        IEventReminderProcessor reminderProcessor,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);

            await ProcessDueRemindersWithoutBlockingDashboardAsync(reminderProcessor, logger, cancellationToken);

            return Results.Ok(await mediator.Send(new GetDashboardSummaryQuery
            {
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId,
                CurrentEquipeId = currentUser.EquipeId
            }, cancellationToken));
        }, logger, "Erro ao buscar resumo do dashboard", "Erro ao buscar resumo do dashboard");
    }

    private static Task<IResult> GetNotifications(
        ClaimsPrincipal claimsPrincipal,
        IEventReminderProcessor reminderProcessor,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);

            await ProcessDueRemindersWithoutBlockingDashboardAsync(reminderProcessor, logger, cancellationToken);

            return Results.Ok(await mediator.Send(new GetDashboardNotificationsQuery
            {
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId,
                CurrentEquipeId = currentUser.EquipeId
            }, cancellationToken));
        }, logger, "Erro ao buscar notificacoes do dashboard", "Erro ao buscar notificacoes do dashboard");
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

    private static CurrentUserContext GetRequiredCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.ToCurrentUserContext()
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
    }
}
