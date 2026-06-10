using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Pacientes.Queries;
using HemodinksAPI.Api.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Features.Dashboard.Queries;

public class GetDashboardSummaryQueryHandler :
    IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>,
    IRequestHandler<GetDashboardNotificationsQuery, IReadOnlyList<DashboardNotificationDto>>
{
    private readonly AppDbContext _context;

    public GetDashboardSummaryQueryHandler(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var usersSummary = request.CurrentPerfilId == Perfil.AdministradorId
            ? await _context.Users
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    UsersCount = group.Count(),
                    ActiveUsersCount = group.Count(user => user.Ativo)
                })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var patientQuery = _context.Pacientes
            .AsNoTracking()
            .AsQueryable();

        patientQuery = PacienteAccess.ApplyScope(
            patientQuery,
            request.CurrentPerfilId,
            request.CurrentUserId);

        var patientSummary = await patientQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                PacientesCount = group.Count(),
                ActivePatientsCount = group.Count(paciente => paciente.User.Ativo),
                PendingPaymentsCount = group.Count(paciente => !paciente.StatusPago),
                PatientFilesCount = group.Sum(paciente => paciente.Arquivos.Count)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var upcomingEventsCount = await ApplyEventScope(
                _context.Events.AsNoTracking(),
                request.CurrentPerfilId,
                request.CurrentUserId)
            .CountAsync(ev => !ev.IsCompleted
                && ev.End >= now
                && ev.Start <= now.AddDays(2), cancellationToken);

        return new DashboardSummaryDto
        {
            UsersCount = usersSummary?.UsersCount ?? 0,
            ActiveUsersCount = usersSummary?.ActiveUsersCount ?? 0,
            PacientesCount = patientSummary?.PacientesCount ?? 0,
            ActivePatientsCount = patientSummary?.ActivePatientsCount ?? 0,
            PendingPaymentsCount = patientSummary?.PendingPaymentsCount ?? 0,
            PatientFilesCount = patientSummary?.PatientFilesCount ?? 0,
            UpcomingEventsCount = upcomingEventsCount
        };
    }

    public async Task<IReadOnlyList<DashboardNotificationDto>> Handle(GetDashboardNotificationsQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 50);
        var patientQuery = _context.Pacientes
            .AsNoTracking()
            .AsQueryable();

        patientQuery = PacienteAccess.ApplyScope(
            patientQuery,
            request.CurrentPerfilId,
            request.CurrentUserId);

        var pendingPatients = await patientQuery
            .Where(paciente => !paciente.StatusPago)
            .OrderByDescending(paciente => paciente.Data ?? paciente.User.DataCadastro)
            .ThenBy(paciente => paciente.Id)
            .Take(limit)
            .Select(paciente => new
            {
                paciente.Id,
                paciente.NomePaciente,
                Medico = paciente.MedicoUser != null ? paciente.MedicoUser.Nome : paciente.Medico,
                paciente.Procedimento,
                Data = paciente.Data ?? paciente.User.DataCadastro
            })
            .ToListAsync(cancellationToken);

        var pendingNotifications = pendingPatients
            .Select(paciente => new DashboardNotificationDto
            {
                Id = paciente.Id,
                Tipo = "PagamentoPendente",
                Titulo = "Pagamento pendente",
                Mensagem = $"Paciente {paciente.NomePaciente} possui pagamento pendente.",
                PacienteId = paciente.Id,
                NomePaciente = paciente.NomePaciente,
                Medico = paciente.Medico,
                Procedimento = paciente.Procedimento,
                Data = paciente.Data
            })
            .ToList();

        var now = DateTime.UtcNow;
        var upcomingEvents = await ApplyEventScope(
                _context.Events.AsNoTracking(),
                request.CurrentPerfilId,
                request.CurrentUserId)
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

        var eventNotifications = upcomingEvents
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
            });

        return pendingNotifications
            .Concat(eventNotifications)
            .OrderBy(notification => notification.Data ?? DateTime.MinValue)
            .Take(limit)
            .ToList();
    }

    private static IQueryable<Event> ApplyEventScope(IQueryable<Event> query, int perfilId, int userId)
    {
        if (perfilId == Perfil.AdministradorId)
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

        return query.Where(ev => ev.UserId == userId);
    }
}
