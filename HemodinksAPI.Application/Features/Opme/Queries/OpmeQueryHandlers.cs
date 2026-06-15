using HemodinksAPI.Application.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Opme.Queries;

public class GetOpmeQueryHandler : IRequestHandler<GetOpmeQuery, List<OpmeDto>>
{
    private readonly IAppDbContext _context;

    public GetOpmeQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<List<OpmeDto>> Handle(GetOpmeQuery request, CancellationToken cancellationToken)
    {
        return await _context.OPME
            .AsNoTracking()
            .OrderBy(item => item.Fornecedor)
            .Select(item => new OpmeDto
            {
                IdFornecedor = item.IdFornecedor,
                Fornecedor = item.Fornecedor
            })
            .ToListAsync(cancellationToken);
    }
}
