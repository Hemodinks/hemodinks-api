using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using MediatR;

namespace HemodinksAPI.Application.Features.Faturamentos.Queries;

public class GetAllFaturamentosMedicosQuery : IRequest<PagedResult<PacienteDto>>
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 100;

    public string? Search { get; set; }

    public string? Medico { get; set; }

    public string? Convenio { get; set; }

    public string? Procedimento { get; set; }

    public int CurrentUserId { get; set; }

    public int CurrentPerfilId { get; set; }

    public DateTime? CompetenciaInicio { get; set; }

    public DateTime? CompetenciaFinal { get; set; }
}
