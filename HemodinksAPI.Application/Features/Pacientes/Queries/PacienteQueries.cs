using HemodinksAPI.Application.Features.Common;
using MediatR;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

public class PacienteDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime? Data { get; set; }
    public DateTime DataCadastro { get; set; }
    public DateTime? DataAtualizacao { get; set; }
    public string NomePaciente { get; set; } = null!;
    public string? Diagnostico { get; set; }
    public string? TratamentoMedico { get; set; }
    public int? HospitalId { get; set; }
    public string? Hospital { get; set; }
    public int? MedicoUserId { get; set; }
    public string? Medico { get; set; }
    public int? MedicoAuxiliar1UserId { get; set; }
    public string? MedicoAuxiliar1 { get; set; }
    public int? MedicoAuxiliar2UserId { get; set; }
    public string? MedicoAuxiliar2 { get; set; }
    public int? ConvenioId { get; set; }
    public string? Convenio { get; set; }
    public int? OpmeFornecedorId { get; set; }
    public string? OpmeFornecedor { get; set; }
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
    public DateTime? DataNascimento { get; set; }
    public bool Ativo { get; set; }
    public int ArquivosCount { get; set; }
    public int ObservacoesNaoLidasCount { get; set; }
    public PacienteFaturamentoDto? Faturamento { get; set; }
    public List<PacienteArquivoDto> Arquivos { get; set; } = [];
}

public class PacienteFaturamentoDto
{
    public int Id { get; set; }
    public int PacienteId { get; set; }
    public decimal? HonorariosCirurgiao { get; set; }
    public decimal? HonorariosAuxiliares { get; set; }
    public decimal? HonorariosAnestesista { get; set; }
    public bool AnestesistaFaturadoSeparado { get; set; }
    public string? Anestesista { get; set; }
    public string? CodigoTussCbhpmAmb { get; set; }
    public string? PorteCirurgicoAnestesico { get; set; }
    public string? GuiaAutorizacaoConvenio { get; set; }
    public string? GuiaInternacaoOuSadt { get; set; }
    public string? OpmeMateriaisEspeciais { get; set; }
    public string? TissXmlStatus { get; set; }
    public decimal? ValorGlosa { get; set; }
    public string? GlosaStatus { get; set; }
    public string? RecursoGlosa { get; set; }
    public bool ConferenciaPagamentoRealizada { get; set; }
    public decimal? RepasseMedico { get; set; }
    public string? RepasseMedicoObservacao { get; set; }
    public string? TipoFaturamentoParticular { get; set; }
    public string? ReciboNotaContrato { get; set; }
    public string? Observacoes { get; set; }
    public DateTime DataCadastro { get; set; }
    public DateTime? DataAtualizacao { get; set; }
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

    public string? SortBy { get; set; }

    public string? SortDirection { get; set; }
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
