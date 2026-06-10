using MediatR;

namespace HemodinksAPI.Api.Features.Convenios.Queries;

public class ConvenioDto
{
    public int IdConvenio { get; set; }
    public string DescricaoConvenio { get; set; } = null!;
}

public class GetConveniosQuery : IRequest<List<ConvenioDto>>
{
}
