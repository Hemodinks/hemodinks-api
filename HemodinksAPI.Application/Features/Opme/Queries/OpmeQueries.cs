using MediatR;

namespace HemodinksAPI.Application.Features.Opme.Queries;

public class OpmeDto
{
    public int IdFornecedor { get; set; }
    public string Fornecedor { get; set; } = null!;
}

public class GetOpmeQuery : IRequest<List<OpmeDto>>
{
}
