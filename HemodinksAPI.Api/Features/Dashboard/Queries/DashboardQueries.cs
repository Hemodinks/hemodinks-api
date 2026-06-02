using MediatR;

namespace HemodinksAPI.Api.Features.Dashboard.Queries;

public class DashboardSummaryDto
{
    public int UsersCount { get; set; }

    public int ActiveUsersCount { get; set; }

    public int PacientesCount { get; set; }

    public int ActivePatientsCount { get; set; }

    public int PendingPaymentsCount { get; set; }

    public int PatientFilesCount { get; set; }
}

public class GetDashboardSummaryQuery : IRequest<DashboardSummaryDto>
{
}
