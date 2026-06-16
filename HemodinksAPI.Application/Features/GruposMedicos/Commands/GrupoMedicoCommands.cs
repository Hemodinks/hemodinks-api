using MediatR;
using HemodinksAPI.Application.Features.GruposMedicos.Queries;

namespace HemodinksAPI.Application.Features.GruposMedicos.Commands;

public class CreateGrupoMedicoCommand : IRequest<GrupoMedicoDto>
{
    public string Nome { get; set; } = null!;

    public bool Ativo { get; set; } = true;

    public List<int> MedicoUserIds { get; set; } = [];
}

public class UpdateGrupoMedicoCommand : IRequest<GrupoMedicoDto>
{
    public int Id { get; set; }

    public string Nome { get; set; } = null!;

    public bool Ativo { get; set; } = true;

    public List<int> MedicoUserIds { get; set; } = [];
}

public class DeleteGrupoMedicoCommand : IRequest
{
    public int Id { get; set; }
}
