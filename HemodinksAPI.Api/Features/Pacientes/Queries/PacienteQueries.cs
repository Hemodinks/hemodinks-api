using HemodinksAPI.Api.Features.Common;
using MediatR;

namespace HemodinksAPI.Api.Features.Pacientes.Queries;

public class PacienteDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime? Data { get; set; }
    public DateTime DataCadastro { get; set; }
    public DateTime? DataAtualizacao { get; set; }
    public string NomePaciente { get; set; } = null!;
    public int? HospitalId { get; set; }
    public string? Hospital { get; set; }
    public int? MedicoUserId { get; set; }
    public string? Medico { get; set; }
    public int? ConvenioId { get; set; }
    public string? Convenio { get; set; }
    public string? CbhpmCodigo { get; set; }
    public string? CbhpmPorte { get; set; }
    public string? Procedimento { get; set; }
    public List<PacienteProcedimentoDto> Procedimentos { get; set; } = [];
    public string? Autorizacao { get; set; }
    public string? Pagamento { get; set; }
    public string? RepasseGlosa { get; set; }
    public bool StatusPago { get; set; }
    public string? Cpf { get; set; }
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public string? FotoPerfil { get; set; }
    public DateTime DataNascimento { get; set; }
    public bool Ativo { get; set; }
    public int ArquivosCount { get; set; }
    public List<PacienteArquivoDto> Arquivos { get; set; } = [];
}

public class PacienteProcedimentoDto
{
    public int Id { get; set; }
    public string? CbhpmCodigo { get; set; }
    public string? CbhpmPorte { get; set; }
    public string Procedimento { get; set; } = null!;
    public decimal? ValorReferencia { get; set; }
    public int Ordem { get; set; }
}

public class PacienteArquivoDto
{
    public int Id { get; set; }
    public string NomeOriginal { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long TamanhoBytes { get; set; }
    public string Url { get; set; } = null!;
    public DateTime DataUpload { get; set; }
}

public class GetAllPacientesQuery : IRequest<PagedResult<PacienteDto>>
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public string? Search { get; set; }

    public string? Medico { get; set; }

    public string? Convenio { get; set; }

    public string? Procedimento { get; set; }

    public int CurrentUserId { get; set; }

    public int CurrentPerfilId { get; set; }
}

public class GetPacienteByIdQuery : IRequest<PacienteDto?>
{
    public int Id { get; set; }

    public int CurrentUserId { get; set; }

    public int CurrentPerfilId { get; set; }

    public GetPacienteByIdQuery(int id, int currentUserId, int currentPerfilId)
    {
        Id = id;
        CurrentUserId = currentUserId;
        CurrentPerfilId = currentPerfilId;
    }
}
