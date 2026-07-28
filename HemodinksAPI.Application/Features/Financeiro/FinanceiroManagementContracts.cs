using HemodinksAPI.Domain.Models;
using MediatR;

namespace HemodinksAPI.Application.Features.Financeiro;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems)
{
    public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);
}

public record ObterAtendimentoQuery(int Id, int CurrentUserId, int CurrentPerfilId) : IRequest<AtendimentoDto>;
public record AtualizarAtendimentoCommand(int Id, DateTime DataProcedimento, int? HospitalId, int? ConvenioId,
    int? OpmeFornecedorId, int MedicoResponsavelId, int? MedicoAuxiliar1Id, int? MedicoAuxiliar2Id, string? Diagnostico,
    string? TratamentoMedico, string? NumeroAutorizacao, AtendimentoCirurgicoStatus Status,
    decimal? ValorGlosa, string? MotivoGlosa, List<AtendimentoProcedimentoInput> Procedimentos,
    int CurrentUserId = 0, int CurrentPerfilId = 0) : IRequest<AtendimentoDto>;
public record ExcluirAtendimentoCommand(int Id, int CurrentUserId = 0, int CurrentPerfilId = 0) : IRequest;
public record ObterFaturamentoQuery(int Id, int CurrentUserId, int CurrentPerfilId) : IRequest<FaturamentoDto>;
public record AtualizarFaturamentoCommand(int Id, string? NumeroGuia, string? NumeroLote, DateTime Competencia,
    string? Observacao, byte[] RowVersion) : IRequest<FaturamentoDto>;
public record AtualizarFaturamentoItemCommand(int FaturamentoId, int ItemId, string? Codigo, string Descricao,
    decimal Quantidade, decimal PesoPercentual, decimal ValorUnitario, byte[] RowVersion) : IRequest<FaturamentoDto>;
public record ExcluirFaturamentoCommand(int Id) : IRequest;
public record AtualizarGlosaCommand(int Id, string? CodigoMotivo, string DescricaoMotivo, decimal ValorGlosado,
    DateTime DataGlosa, string? Observacao) : IRequest<FaturamentoDto>;
public record ExcluirGlosaCommand(int Id) : IRequest<FaturamentoDto>;
public record AtualizarRecursoGlosaCommand(int Id, DateTime? DataEnvio, string Justificativa,
    decimal ValorRecorrido, DateTime? DataResposta, decimal ValorRecuperado, RecursoGlosaStatus Status,
    string? Observacao) : IRequest<FaturamentoDto>;
public record ExcluirRecursoGlosaCommand(int Id) : IRequest<FaturamentoDto>;
public record ObterContaReceberQuery(int Id) : IRequest<ContaReceberDto>;
public record AtualizarContaReceberCommand(int Id, string NumeroDocumento, string Descricao, DateTime DataEmissao,
    DateTime DataVencimento, decimal ValorOriginal, decimal ValorAjustado, string? Observacao,
    byte[] RowVersion) : IRequest<ContaReceberDto>;
public record CancelarContaReceberCommand(int Id, string Motivo, byte[] RowVersion) : IRequest<ContaReceberDto>;
public record ExcluirConvenioProcedimentoPrecoCommand(int Id) : IRequest;
public record PesquisarContasReceberQuery(int Page = 1, int PageSize = 25, string? Termo = null,
    ContaReceberStatus? Status = null, DateTime? VencimentoInicio = null, DateTime? VencimentoFim = null,
    int? ConvenioId = null, int? MedicoId = null, int? PacienteId = null) : IRequest<PagedResult<ContaReceberDto>>;
public record PesquisarFaturamentosQuery(int Page, int PageSize, string? Termo, FaturamentoStatus? Status,
    DateTime? CompetenciaInicio, DateTime? CompetenciaFim, int? ConvenioId, int CurrentUserId,
    int CurrentPerfilId) : IRequest<PagedResult<FaturamentoDto>>;
public record FinanceiroResumoDto(decimal ValorApresentado, decimal ValorGlosado, decimal ValorRecuperado,
    decimal ValorReconhecido, decimal ValorRecebido, decimal SaldoAberto, decimal ValorVencido,
    decimal RecebimentosPeriodo, int TitulosVencidos,
    IReadOnlyList<FinanceiroResumoMensalDto> PorCompetencia);
public record FinanceiroResumoMensalDto(DateTime Competencia, decimal Apresentado, decimal Reconhecido,
    decimal Recebido, decimal SaldoAberto);
public record ObterFinanceiroResumoQuery(DateTime? Inicio, DateTime? Fim, int? ConvenioId, int? MedicoId,
    int? PacienteId) : IRequest<FinanceiroResumoDto>;
public record PacienteFinanceiroResumoDto(decimal ValorApresentado, decimal ValorGlosado, decimal ValorReconhecido,
    decimal ValorRecebido, decimal SaldoAberto, string StatusFinanceiro, string OrigemDados,
    IReadOnlyList<string> Avisos);
public record ObterPacienteFinanceiroResumoQuery(int PacienteId, int CurrentUserId, int CurrentPerfilId)
    : IRequest<PacienteFinanceiroResumoDto>;
