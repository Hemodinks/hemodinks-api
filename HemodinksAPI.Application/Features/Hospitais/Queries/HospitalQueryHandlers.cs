using HemodinksAPI.Application.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Hospitais.Queries;

public class GetHospitaisQueryHandler : IRequestHandler<GetHospitaisQuery, List<HospitalDto>>
{
    private readonly IAppDbContext _context;

    public GetHospitaisQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<List<HospitalDto>> Handle(GetHospitaisQuery request, CancellationToken cancellationToken)
    {
        return await _context.Hospitais
            .AsNoTracking()
            .OrderBy(hospital => hospital.Nome)
            .Select(hospital => new HospitalDto
            {
                Id = hospital.Id,
                Nome = hospital.Nome
            })
            .ToListAsync(cancellationToken);
    }
}
