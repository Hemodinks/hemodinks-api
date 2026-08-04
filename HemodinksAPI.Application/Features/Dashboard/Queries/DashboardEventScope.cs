using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Dashboard.Queries;

internal static class DashboardEventScope
{
    public static async Task<int> CountUpcomingEventsAsync(
        IAppDbContext context,
        ILogger logger,
        int perfilId,
        int userId,
        int? equipeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;

            return await ApplyEventScope(context, context.Events.AsNoTracking(), perfilId, userId, equipeId)
                .CountAsync(ev => !ev.IsCompleted
                    && ev.End >= now
                    && ev.Start <= now.AddDays(2), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao calcular proximos eventos do dashboard");
            return 0;
        }
    }

    public static async Task<IReadOnlyList<DashboardNotificationDto>> GetUpcomingEventNotificationsAsync(
        IAppDbContext context,
        ILogger logger,
        int perfilId,
        int userId,
        int? equipeId,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;

            var upcomingEvents = await ApplyEventScope(context, context.Events.AsNoTracking(), perfilId, userId, equipeId)
                .Where(ev => !ev.IsCompleted
                    && ev.End >= now
                    && ev.Start <= now.AddDays(2))
                .OrderBy(ev => ev.Start)
                .ThenBy(ev => ev.Title)
                .Take(limit)
                .Select(ev => new
                {
                    ev.Id,
                    ev.Title,
                    ev.Description,
                    ev.Start,
                    MedicalUserName = ev.MedicalUser != null ? ev.MedicalUser.Nome : null
                })
                .ToListAsync(cancellationToken);

            return upcomingEvents
                .Select(ev => new DashboardNotificationDto
                {
                    Id = ev.Id,
                    EventId = ev.Id,
                    Tipo = "EventoAgenda",
                    Titulo = "Evento da agenda",
                    Mensagem = string.IsNullOrWhiteSpace(ev.Description)
                        ? ev.Title
                        : $"{ev.Title}: {ev.Description}",
                    PacienteId = 0,
                    NomePaciente = string.Empty,
                    Medico = ev.MedicalUserName,
                    Data = ev.Start
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar eventos proximos para notificacoes do dashboard");
            return [];
        }
    }

    private static IQueryable<Event> ApplyEventScope(
        IAppDbContext context,
        IQueryable<Event> query,
        int perfilId,
        int userId,
        int? equipeId)
    {
        if (Perfil.IsAdministradorOuSuper(perfilId))
        {
            return query;
        }

        if (perfilId == Perfil.MedicosId)
        {
            return query.Where(ev =>
                ev.UserId == userId
                || ev.MedicalUserId == userId
                || (ev.NotifyMedicalProfile && ev.MedicalUserId == null));
        }

        if (perfilId == Perfil.EquipeId && equipeId.HasValue)
        {
            var memberUserIds = context.EquipeMembros.AsNoTracking()
                .Where(member => member.EquipeId == equipeId && member.Ativo)
                .Select(member => member.UserId);
            return query.Where(ev => ev.UserId == userId
                || (ev.MedicalUserId.HasValue && memberUserIds.Contains(ev.MedicalUserId.Value)));
        }

        return query.Where(ev => ev.UserId == userId);
    }
}
