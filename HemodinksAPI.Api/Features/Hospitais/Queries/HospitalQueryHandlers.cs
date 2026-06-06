using HemodinksAPI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Features.Hospitais.Queries;

public class GetHospitaisQueryHandler : IRequestHandler<GetHospitaisQuery, List<HospitalDto>>
{
    private readonly AppDbContext _context;

    public GetHospitaisQueryHandler(AppDbContext context)
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
