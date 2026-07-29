using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Dashboard.Queries;

public class GetDashboardSummaryQueryHandler : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetDashboardSummaryQueryHandler> _logger;

    public GetDashboardSummaryQueryHandler(
        IAppDbContext context,
        ILogger<GetDashboardSummaryQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var usersSummary = Perfil.IsAdministradorOuSuper(request.CurrentPerfilId)
            ? await _context.Users
                .AsNoTracking()
                .Where(user => user.PerfilId != Perfil.PacientesId)
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    UsersCount = group.Count(),
                    ActiveUsersCount = group.Count(user => user.Ativo)
                })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var patientSummary = await BuildPatientSummaryAsync(
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);
        var pendingPaymentsCount = await CountPendingPaymentsAsync(
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);

        var upcomingEventsCount = await DashboardEventScope.CountUpcomingEventsAsync(
            _context,
            _logger,
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);

        var unreadObservationCount = request.CurrentPerfilId == Perfil.PacientesId
            ? 0
            : await _context.Observacoes
                .AsNoTracking()
                .CountAsync(observacao =>
                    observacao.DestinatarioUserId == request.CurrentUserId
                    && observacao.DataLeitura == null,
                    cancellationToken);

        var unreadAgendaNotificationCount = await _context.AgendaNotifications
            .AsNoTracking()
            .CountAsync(notification =>
                notification.RecipientUserId == request.CurrentUserId
                && notification.ReadAt == null,
                cancellationToken);

        return new DashboardSummaryDto
        {
            UsersCount = usersSummary?.UsersCount ?? 0,
            ActiveUsersCount = usersSummary?.ActiveUsersCount ?? 0,
            PacientesCount = patientSummary?.PacientesCount ?? 0,
            ActivePatientsCount = patientSummary?.ActivePatientsCount ?? 0,
            PendingPaymentsCount = pendingPaymentsCount,
            PatientFilesCount = patientSummary?.PatientFilesCount ?? 0,
            UpcomingEventsCount = upcomingEventsCount,
            UnreadObservationCount = unreadObservationCount,
            UnreadAgendaNotificationCount = unreadAgendaNotificationCount
        };
    }

    private async Task<dynamic?> BuildPatientSummaryAsync(
        int perfilId,
        int userId,
        CancellationToken cancellationToken)
    {
        var patientQuery = PacienteAccess.ApplyScope(
            _context,
            _context.Pacientes.AsNoTracking().AsQueryable(),
            perfilId,
            userId);

        return await patientQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                PacientesCount = group.Count(),
                ActivePatientsCount = group.Count(paciente => paciente.User.Ativo),
                PatientFilesCount = group.Sum(paciente => paciente.Arquivos.Count)
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<int> CountPendingPaymentsAsync(
        int perfilId,
        int userId,
        CancellationToken cancellationToken)
    {
        var query = _context.ContasReceber
            .AsNoTracking()
            .Where(conta =>
                conta.Status == ContaReceberStatus.Aberto
                || conta.Status == ContaReceberStatus.ParcialmenteRecebido
                || conta.Status == ContaReceberStatus.Vencido);

        if (perfilId == Perfil.MedicosId)
        {
            query = query.Where(conta =>
                conta.Faturamento.AtendimentoCirurgico.MedicoResponsavelId == userId
                || conta.Faturamento.AtendimentoCirurgico.MedicoAuxiliar1Id == userId
                || conta.Faturamento.AtendimentoCirurgico.MedicoAuxiliar2Id == userId);
        }
        else if (perfilId == Perfil.PacientesId)
        {
            query = query.Where(conta => conta.Paciente.UserId == userId);
        }

        return query.CountAsync(cancellationToken);
    }
}
