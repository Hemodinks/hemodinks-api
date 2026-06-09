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

        return new DashboardSummaryDto
        {
            UsersCount = usersSummary?.UsersCount ?? 0,
            ActiveUsersCount = usersSummary?.ActiveUsersCount ?? 0,
            PacientesCount = patientSummary?.PacientesCount ?? 0,
            ActivePatientsCount = patientSummary?.ActivePatientsCount ?? 0,
            PendingPaymentsCount = patientSummary?.PendingPaymentsCount ?? 0,
            PatientFilesCount = patientSummary?.PatientFilesCount ?? 0
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
}
