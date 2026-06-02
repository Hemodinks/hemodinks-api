using HemodinksAPI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Features.Dashboard.Queries;

public class GetDashboardSummaryQueryHandler : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    private readonly AppDbContext _context;

    public GetDashboardSummaryQueryHandler(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var usersSummary = await _context.Users
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                UsersCount = group.Count(),
                ActiveUsersCount = group.Count(user => user.Ativo)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var patientSummary = await _context.Pacientes
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                PacientesCount = group.Count(),
                ActivePatientsCount = group.Count(paciente => paciente.User.Ativo),
                PendingPaymentsCount = group.Count(paciente => !paciente.StatusPago)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new DashboardSummaryDto
        {
            UsersCount = usersSummary?.UsersCount ?? 0,
            ActiveUsersCount = usersSummary?.ActiveUsersCount ?? 0,
            PacientesCount = patientSummary?.PacientesCount ?? 0,
            ActivePatientsCount = patientSummary?.ActivePatientsCount ?? 0,
            PendingPaymentsCount = patientSummary?.PendingPaymentsCount ?? 0,
            PatientFilesCount = await _context.PacienteArquivos.AsNoTracking().CountAsync(cancellationToken)
        };
    }
}
