using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Dashboard.Queries;

public class GetDashboardNotificationsQueryHandler : IRequestHandler<GetDashboardNotificationsQuery, IReadOnlyList<DashboardNotificationDto>>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetDashboardNotificationsQueryHandler> _logger;

    public GetDashboardNotificationsQueryHandler(
        IAppDbContext context,
        ILogger<GetDashboardNotificationsQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DashboardNotificationDto>> Handle(GetDashboardNotificationsQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 50);
        var pendingNotifications = await GetPendingPaymentNotificationsAsync(request, limit, cancellationToken);
        var eventNotifications = await DashboardEventScope.GetUpcomingEventNotificationsAsync(
            _context,
            _logger,
            request.CurrentPerfilId,
            request.CurrentUserId,
            limit,
            cancellationToken);
        var observationNotifications = await GetObservationNotificationsAsync(request, limit, cancellationToken);
        var agendaNotifications = await GetAgendaNotificationsAsync(request.CurrentUserId, limit, cancellationToken);

        return pendingNotifications
            .Concat(eventNotifications)
            .Concat(observationNotifications)
            .Concat(agendaNotifications)
            .OrderByDescending(notification => notification.Data ?? DateTime.MinValue)
            .Take(limit)
            .ToList();
    }

    private async Task<List<DashboardNotificationDto>> GetPendingPaymentNotificationsAsync(
        GetDashboardNotificationsQuery request,
        int limit,
        CancellationToken cancellationToken)
    {
        var patientQuery = PacienteAccess.ApplyScope(
            _context,
            _context.Pacientes.AsNoTracking().AsQueryable(),
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

        return pendingPatients
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
    }

    private async Task<IReadOnlyList<DashboardNotificationDto>> GetObservationNotificationsAsync(
        GetDashboardNotificationsQuery request,
        int limit,
        CancellationToken cancellationToken)
    {
        if (request.CurrentPerfilId == Perfil.PacientesId)
        {
            return [];
        }

        return await _context.Observacoes
            .AsNoTracking()
            .Where(observacao =>
                observacao.DestinatarioUserId == request.CurrentUserId
                && observacao.DataLeitura == null)
            .OrderByDescending(observacao => observacao.DataCadastro)
            .ThenByDescending(observacao => observacao.Id)
            .Take(limit)
            .Select(observacao => new DashboardNotificationDto
            {
                Id = observacao.Id,
                ObservacaoId = observacao.Id,
                Tipo = "ObservacaoPaciente",
                Titulo = "Observacao do paciente",
                Mensagem = observacao.Texto,
                PacienteId = observacao.PacienteId,
                NomePaciente = observacao.Paciente.NomePaciente,
                Medico = observacao.Medico,
                Autor = observacao.AutorUser.Nome,
                Data = observacao.DataCadastro,
                DataLeitura = observacao.DataLeitura
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DashboardNotificationDto>> GetAgendaNotificationsAsync(
        int currentUserId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await _context.AgendaNotifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == currentUserId)
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(limit)
            .Select(notification => new DashboardNotificationDto
            {
                Id = notification.Id,
                Tipo = "NotificacaoAgenda",
                Titulo = notification.Title,
                Mensagem = notification.Message,
                PacienteId = 0,
                NomePaciente = string.Empty,
                Autor = notification.SenderUser.Nome,
                Data = notification.CreatedAt,
                DataLeitura = notification.ReadAt
            })
            .ToListAsync(cancellationToken);
    }
}
