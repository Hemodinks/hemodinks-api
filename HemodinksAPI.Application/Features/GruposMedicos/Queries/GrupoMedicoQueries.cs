using HemodinksAPI.Application.Features.Common;
using MediatR;

namespace HemodinksAPI.Application.Features.GruposMedicos.Queries;

public class GrupoMedicoDto
{
    public int Id { get; set; }

    public string Nome { get; set; } = null!;

    public bool Ativo { get; set; }

    public DateTime DataCadastro { get; set; }

    public DateTime? DataAtualizacao { get; set; }

    public int MembrosCount { get; set; }

    public List<GrupoMedicoMembroDto> Membros { get; set; } = [];
}

public class GrupoMedicoMembroDto
{
    public int UserId { get; set; }

    public string Nome { get; set; } = null!;

    public string Email { get; set; } = null!;
}

public class MedicalUserOptionDto
{
    public int Id { get; set; }

    public string Nome { get; set; } = null!;

    public string Email { get; set; } = null!;
}

public class GetAllGruposMedicosQuery : IRequest<PagedResult<GrupoMedicoDto>>
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public string? Search { get; set; }

    public string? SortBy { get; set; }

    public string? SortDirection { get; set; }
    public int? CurrentEquipeId { get; set; }
}

public class GetGrupoMedicoByIdQuery : IRequest<GrupoMedicoDto?>
{
    public int Id { get; set; }
    public int? CurrentEquipeId { get; set; }

    public GetGrupoMedicoByIdQuery(int id)
    {
        Id = id;
    }
}

public class GetScopedMedicalUsersQuery : IRequest<List<MedicalUserOptionDto>>
{
    public int CurrentPerfilId { get; set; }

    public int CurrentUserId { get; set; }
    public int? CurrentEquipeId { get; set; }
}
