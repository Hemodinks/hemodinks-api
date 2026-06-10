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

    public int UpcomingEventsCount { get; set; }
}

public class DashboardNotificationDto
{
    public int Id { get; set; }

    public string Tipo { get; set; } = string.Empty;

    public string Titulo { get; set; } = string.Empty;

    public string Mensagem { get; set; } = string.Empty;

    public int PacienteId { get; set; }

    public int? EventId { get; set; }

    public string NomePaciente { get; set; } = string.Empty;

    public string? Medico { get; set; }

    public string? Procedimento { get; set; }

    public DateTime? Data { get; set; }
}

public class GetDashboardSummaryQuery : IRequest<DashboardSummaryDto>
{
    public int CurrentUserId { get; set; }

    public int CurrentPerfilId { get; set; }
}

public class GetDashboardNotificationsQuery : IRequest<IReadOnlyList<DashboardNotificationDto>>
{
    public int CurrentUserId { get; set; }

    public int CurrentPerfilId { get; set; }

    public int Limit { get; set; } = 20;
}
