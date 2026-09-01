using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Dashboard.Queries;

public class GetDashboardSummaryQueryHandler : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    private readonly IDashboardFeatureDbContext _context;
    private readonly ILogger<GetDashboardSummaryQueryHandler> _logger;

    public GetDashboardSummaryQueryHandler(
        IDashboardFeatureDbContext context,
        ILogger<GetDashboardSummaryQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var usersQuery = _context.Users.AsNoTracking().Where(user => user.PerfilId != Perfil.PacientesId);
        if (request.CurrentPerfilId == Perfil.EquipeId && request.CurrentEquipeId.HasValue)
        {
            var memberUserIds = _context.EquipeMembros.AsNoTracking()
                .Where(member => member.EquipeId == request.CurrentEquipeId && member.Ativo)
                .Select(member => member.UserId);
            usersQuery = usersQuery.Where(user => memberUserIds.Contains(user.Id));
        }
        var canSeeUsersSummary = Perfil.IsAdministradorOuSuper(request.CurrentPerfilId)
            || request.CurrentPerfilId == Perfil.EquipeId;
        var usersSummary = canSeeUsersSummary
            ? await usersQuery.GroupBy(_ => 1).Select(group => new
            {
                UsersCount = group.Count(),
                ActiveUsersCount = group.Count(user => user.Ativo)
            }).FirstOrDefaultAsync(cancellationToken)
            : null;

        var patientSummary = await BuildPatientSummaryAsync(
            request.CurrentPerfilId,
            request.CurrentUserId,
            request.CurrentEquipeId,
            cancellationToken);
        var pendingPaymentsCount = await CountPendingPaymentsAsync(
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);
        var attendancesCount = await CountAttendancesAsync(
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);
        var billingsCount = await CountBillingsAsync(
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);

        var upcomingEventsCount = await DashboardEventScope.CountUpcomingEventsAsync(
            _context,
            _logger,
            request.CurrentPerfilId,
            request.CurrentUserId,
            request.CurrentEquipeId,
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
            AttendancesCount = attendancesCount,
            BillingsCount = billingsCount,
            PatientFilesCount = patientSummary?.PatientFilesCount ?? 0,
            UpcomingEventsCount = upcomingEventsCount,
            UnreadObservationCount = unreadObservationCount,
            UnreadAgendaNotificationCount = unreadAgendaNotificationCount
        };
    }

    private async Task<dynamic?> BuildPatientSummaryAsync(
        int perfilId,
        int userId,
        int? equipeId,
        CancellationToken cancellationToken)
    {
        var patientQuery = PacienteAccess.ApplyScope(
            _context,
            _context.Pacientes.AsNoTracking().AsQueryable(),
            perfilId,
            userId,
            equipeId);

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

    private Task<int> CountAttendancesAsync(
        int perfilId,
        int userId,
        CancellationToken cancellationToken)
    {
        var query = _context.AtendimentosCirurgicos.AsNoTracking();

        if (perfilId == Perfil.MedicosId)
        {
            query = query.Where(atendimento =>
                atendimento.MedicoResponsavelId == userId
                || atendimento.MedicoAuxiliar1Id == userId
                || atendimento.MedicoAuxiliar2Id == userId);
        }
        else if (perfilId == Perfil.PacientesId)
        {
            query = query.Where(atendimento => atendimento.Paciente.UserId == userId);
        }

        return query.CountAsync(cancellationToken);
    }

    private Task<int> CountBillingsAsync(
        int perfilId,
        int userId,
        CancellationToken cancellationToken)
    {
        var query = _context.Faturamentos.AsNoTracking();

        if (perfilId == Perfil.MedicosId)
        {
            query = query.Where(faturamento =>
                faturamento.AtendimentoCirurgico.MedicoResponsavelId == userId
                || faturamento.AtendimentoCirurgico.MedicoAuxiliar1Id == userId
                || faturamento.AtendimentoCirurgico.MedicoAuxiliar2Id == userId);
        }
        else if (perfilId == Perfil.PacientesId)
        {
            query = query.Where(faturamento =>
                faturamento.AtendimentoCirurgico.Paciente.UserId == userId);
        }

        return query.CountAsync(cancellationToken);
    }
}
