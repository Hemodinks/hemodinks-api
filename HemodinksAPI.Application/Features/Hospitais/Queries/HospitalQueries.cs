using MediatR;

namespace HemodinksAPI.Api.Features.Hospitais.Queries;

public class HospitalDto
{
    public int Id { get; set; }

    public string Nome { get; set; } = null!;
}

public class GetHospitaisQuery : IRequest<List<HospitalDto>>
{
}
