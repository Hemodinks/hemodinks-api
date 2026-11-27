using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Faturamentos.Queries;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Dashboard.Queries;

public class GetDashboardSummaryQueryHandler : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    private readonly IDashboardFeatureDbContext _context;
    private readonly ILogger<GetDashboardSummaryQueryHandler> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IClinicDirectoryDbContext? _clinicDirectory;
    private readonly IClinicaContext? _clinicaContext;

    public GetDashboardSummaryQueryHandler(
        IDashboardFeatureDbContext context,
        ILogger<GetDashboardSummaryQueryHandler> logger,
        TimeProvider? timeProvider = null,
        IClinicDirectoryDbContext? clinicDirectory = null,
        IClinicaContext? clinicaContext = null)
    {
        _context = context;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _clinicDirectory = clinicDirectory;
        _clinicaContext = clinicaContext;
    }

    // Mesmos perfis da politica FaturamentoMedicoVisualizar: paciente nao recebe valores financeiros.
    private static bool CanSeeSurgicalBilling(int perfilId) =>
        Perfil.IsAdministradorOuSuper(perfilId)
        || perfilId is Perfil.MedicosId or Perfil.ControllerId or Perfil.EquipeId;

    // Mesma regra do ClinicaModuleAccessMiddleware para /api/faturamentos-medicos: o plano precisa do
    // modulo Faturamento; Administrador e SuperAdministrador sao dispensados. Sem dados para checar, nega.
    private async Task<bool> HasBillingModuleAsync(int perfilId, CancellationToken cancellationToken)
    {
        if (Perfil.IsAdministradorOuSuper(perfilId)) return true;
        if (_clinicDirectory == null || _clinicaContext?.ClinicaId is not { } clinicaId) return false;
        var subscription = await _clinicDirectory.Clinicas.AsNoTracking()
            .Where(item => item.Id == clinicaId)
            .Select(item => new { item.Plano, item.ModulosLiberados })
            .SingleOrDefaultAsync(cancellationToken);
        return subscription != null && ClinicaModulos.GetEffective(subscription.Plano, subscription.ModulosLiberados)
            .Contains(ClinicaModulos.Faturamento, StringComparer.OrdinalIgnoreCase);
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

        var surgicalBilling = CanSeeSurgicalBilling(request.CurrentPerfilId)
            && await HasBillingModuleAsync(request.CurrentPerfilId, cancellationToken)
            ? await BuildSurgicalBillingAsync(request.CurrentPerfilId, request.CurrentUserId, request.CurrentEquipeId, cancellationToken)
            : null;

        return new DashboardSummaryDto
        {
            CirurgiasNaoConsolidadasCount = surgicalBilling?.NaoConsolidadas,
            CirurgiasAReceberCount = surgicalBilling?.AReceber,
            ValorAReceber = surgicalBilling?.ValorAReceber,
            FaturamentoMensal = surgicalBilling?.Mensal,
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
            _context.Pacientes.AsNoTracking().Where(paciente => paciente.Ativo),
            perfilId,
            userId,
            equipeId);

        return await patientQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                PacientesCount = group.Count(),
                ActivePatientsCount = group.Count(paciente => paciente.User.Ativo),
                PatientFilesCount = group.Sum(paciente => paciente.Arquivos.Count(a => a.ExcluidoEm == null))
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record SurgicalBillingSummary(
        int NaoConsolidadas, int AReceber, decimal ValorAReceber, IReadOnlyList<DashboardMonthlyBillingDto> Mensal);

    private async Task<SurgicalBillingSummary> BuildSurgicalBillingAsync(
        int perfilId,
        int userId,
        int? equipeId,
        CancellationToken cancellationToken)
    {
        // Mesmo recorte do modulo de faturamento (FaturamentoMedicoScope), nao o de pacientes: o medico so
        // ve valores das proprias cirurgias, nunca as de colegas do grupo medico. Tenant pelo filtro global.
        var cirurgias = FaturamentoMedicoScope.ApplyScope(
            _context.EquipeMembros,
            _context.Pacientes.AsNoTracking().Where(paciente => paciente.Ativo),
            perfilId,
            userId,
            equipeId);

        var naoConsolidadas = await cirurgias.CountAsync(paciente => paciente.DataAtendimento == null, cancellationToken);
        var aReceber = cirurgias.Where(paciente => paciente.DataAtendimento != null && !paciente.StatusPago);
        var aReceberCount = await aReceber.CountAsync(cancellationToken);
        var valorAReceber = await aReceber
            .SumAsync(paciente => (paciente.FaturamentoMedico!.HonorariosCirurgiao ?? 0m)
                - (paciente.FaturamentoMedico!.ValorGlosa ?? 0m), cancellationToken);

        var mesAtual = _timeProvider.GetUtcNow().UtcDateTime;
        var inicio = new DateTime(mesAtual.Year, mesAtual.Month, 1).AddMonths(-11);
        var fim = new DateTime(mesAtual.Year, mesAtual.Month, 1).AddMonths(1);
        var porMes = await cirurgias
            .Where(paciente => paciente.DataAtendimento >= inicio && paciente.DataAtendimento < fim)
            .GroupBy(paciente => new { paciente.DataAtendimento!.Value.Year, paciente.DataAtendimento!.Value.Month })
            .Select(grupo => new
            {
                grupo.Key.Year,
                grupo.Key.Month,
                Cirurgias = grupo.Count(),
                Faturado = grupo.Sum(paciente => paciente.FaturamentoMedico!.HonorariosCirurgiao ?? 0m),
                Recebido = grupo.Sum(paciente => paciente.StatusPago ? paciente.FaturamentoMedico!.RepasseMedico ?? 0m : 0m)
            })
            .ToListAsync(cancellationToken);

        var mensal = Enumerable.Range(0, 12)
            .Select(offset => inicio.AddMonths(offset))
            .Select(mes => porMes.FirstOrDefault(item => item.Year == mes.Year && item.Month == mes.Month) is { } item
                ? new DashboardMonthlyBillingDto(mes.Year, mes.Month, item.Cirurgias, item.Faturado, item.Recebido)
                : new DashboardMonthlyBillingDto(mes.Year, mes.Month, 0, 0m, 0m))
            .ToList();

        return new SurgicalBillingSummary(naoConsolidadas, aReceberCount, valorAReceber, mensal);
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
