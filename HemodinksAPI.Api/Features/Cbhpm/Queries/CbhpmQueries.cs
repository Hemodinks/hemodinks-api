using HemodinksAPI.Api.Features.Common;
using MediatR;

namespace HemodinksAPI.Api.Features.Cbhpm.Queries;

public class CbhpmGeralDto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Procedimento { get; set; } = null!;
    public string? Porte { get; set; }
    public decimal? CustoOperacional { get; set; }
    public string? Capitulo { get; set; }
    public string? Grupo { get; set; }
    public int? PaginaPdf { get; set; }
}

public class GetCbhpmGeralQuery : IRequest<PagedResult<CbhpmGeralDto>>
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public string? Search { get; set; }

    public string? Codigo { get; set; }

    public string? Procedimento { get; set; }

    public string? Porte { get; set; }
}
