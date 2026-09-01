using HemodinksAPI.Application.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Convenios.Queries;

public class GetConveniosQueryHandler : IRequestHandler<GetConveniosQuery, List<ConvenioDto>>
{
    private readonly ICatalogQueryDbContext _context;

    public GetConveniosQueryHandler(ICatalogQueryDbContext context)
    {
        _context = context;
    }

    public async Task<List<ConvenioDto>> Handle(GetConveniosQuery request, CancellationToken cancellationToken)
    {
        return await _context.Convenios
            .AsNoTracking()
            .OrderBy(convenio => convenio.DescricaoConvenio)
            .Select(convenio => new ConvenioDto
            {
                IdConvenio = convenio.IdConvenio,
                DescricaoConvenio = convenio.DescricaoConvenio
            })
            .ToListAsync(cancellationToken);
    }
}
