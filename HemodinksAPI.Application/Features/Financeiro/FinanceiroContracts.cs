using HemodinksAPI.Domain.Models;
using MediatR;

namespace HemodinksAPI.Application.Features.Financeiro;

public record AtendimentoProcedimentoInput(string? CbhpmCodigo, string? Descricao, decimal Quantidade = 1m,
    decimal PesoPercentual = 100m, string? CbhpmPorte = null);

public record CriarAtendimentoCommand(
    int PacienteId, DateTime DataProcedimento, int? HospitalId, int? ConvenioId, int? OpmeFornecedorId,
    string? Hospital, string? Convenio, string? OpmeFornecedor,
    int MedicoResponsavelId, int? MedicoAuxiliar1Id, int? MedicoAuxiliar2Id,
    string? Diagnostico, string? TratamentoMedico, string? NumeroAutorizacao,
    decimal? ValorGlosa, string? MotivoGlosa, string? Observacao,
    AtendimentoCirurgicoStatus Status, List<AtendimentoProcedimentoInput> Procedimentos) : IRequest<AtendimentoDto>
{
    public int CurrentUserId { get; init; }
    public int CurrentPerfilId { get; init; }
}

public record CriarFaturamentoCommand(
    int AtendimentoCirurgicoId, string? NumeroGuia, string? NumeroLote,
    DateTime Competencia, string? Observacao) : IRequest<FaturamentoDto>;
public record AtualizarStatusFaturamentoCommand(int Id, FaturamentoStatus Status, byte[] RowVersion) : IRequest<FaturamentoDto>;
public record RetornoFaturamentoItemInput(int FaturamentoItemId, decimal ValorGlosado, decimal ValorAprovado,
    string? CodigoMotivo, string? MotivoGlosa);
public record RegistrarRetornoFaturamentoCommand(int Id, DateTime DataRetorno,
    List<RetornoFaturamentoItemInput> Itens, byte[] RowVersion) : IRequest<FaturamentoDto>;
public record RegistrarGlosaCommand(int FaturamentoId, int? FaturamentoItemId, string? CodigoMotivo,
    string DescricaoMotivo, decimal ValorGlosado, DateTime DataGlosa, string? Observacao) : IRequest<FaturamentoDto>;
public record RegistrarRecursoGlosaCommand(int GlosaId, DateTime? DataEnvio, string Justificativa,
    decimal ValorRecorrido, DateTime? DataResposta, decimal ValorRecuperado, RecursoGlosaStatus Status, string? Observacao) : IRequest<FaturamentoDto>;
public record GerarContaReceberCommand(int FaturamentoId, string NumeroDocumento, string Descricao,
    DateTime DataEmissao, DateTime DataVencimento, decimal? ValorOriginal, decimal? ValorAjustado, string? Observacao) : IRequest<ContaReceberDto>;
public record RegistrarRecebimentoCommand(int ContaReceberId, DateTime DataRecebimento, decimal ValorRecebido,
    FormaRecebimento FormaRecebimento, string? ReferenciaBancaria, string? DocumentoComprovante,
    string? Observacao, int UsuarioCadastroId, byte[] RowVersion) : IRequest<ContaReceberDto>;
public record EstornarRecebimentoCommand(int RecebimentoId, string MotivoEstorno, int UsuarioEstornoId) : IRequest<ContaReceberDto>;
public record ListarAtendimentosQuery(int? PacienteId = null, int CurrentUserId = 0, int CurrentPerfilId = 0) : IRequest<List<AtendimentoDto>>;
public record ListarFaturamentosQuery(int CurrentUserId = 0, int CurrentPerfilId = 0) : IRequest<List<FaturamentoDto>>;
public record ListarContasReceberQuery() : IRequest<List<ContaReceberDto>>;
public record SalvarConvenioProcedimentoPrecoCommand(int? Id, int ConvenioId, string CbhpmCodigo,
    decimal ValorNegociado, decimal PercentualPrincipal, decimal PercentualAuxiliar1, decimal PercentualAuxiliar2,
    DateTime VigenciaInicio, DateTime? VigenciaFinal, bool Ativo) : IRequest<ConvenioProcedimentoPrecoDto>;
public record ListarConvenioProcedimentoPrecosQuery(int? ConvenioId = null, string? CbhpmCodigo = null)
    : IRequest<List<ConvenioProcedimentoPrecoDto>>;

public record AtendimentoProcedimentoDto(int Id, string? CbhpmCodigo, string? CbhpmPorte, string Descricao,
    decimal Quantidade, decimal PesoPercentual, decimal? ValorReferencia, decimal? ValorNegociado, int Ordem);
public record AtendimentoArquivoDto(int Id, string NomeOriginal, string ContentType, long TamanhoBytes,
    string Url, DateTime DataUpload);
public record AtendimentoDto(int Id, int PacienteId, string Paciente, DateTime DataProcedimento, int? HospitalId,
    int? ConvenioId, int? OpmeFornecedorId, string? OpmeFornecedor, int MedicoResponsavelId,
    int? MedicoAuxiliar1Id, int? MedicoAuxiliar2Id,
    string? Diagnostico, string? TratamentoMedico, string? NumeroAutorizacao,
    decimal? ValorGlosa, string? MotivoGlosa, string? Observacao,
    AtendimentoCirurgicoStatus Status, DateTime DataCadastro, DateTime? DataAtualizacao,
    List<AtendimentoProcedimentoDto> Procedimentos, List<AtendimentoArquivoDto> Arquivos);
public record FaturamentoItemDto(int Id, int? AtendimentoProcedimentoId, string? Codigo, string Descricao,
    decimal Quantidade, decimal PesoPercentual, decimal ValorUnitario, decimal ValorApresentado,
    decimal ValorGlosado, decimal ValorAprovado, FaturamentoItemStatus Status, int Ordem);
public record RecursoGlosaDto(int Id, DateTime? DataEnvio, string Justificativa, decimal ValorRecorrido,
    DateTime? DataResposta, decimal ValorRecuperado, RecursoGlosaStatus Status, string? Observacao);
public record GlosaDto(int Id, int? FaturamentoItemId, string? CodigoMotivo, string DescricaoMotivo,
    decimal ValorGlosado, DateTime DataGlosa, GlosaStatus Status, string? Observacao,
    List<RecursoGlosaDto> Recursos);
public record FaturamentoDto(int Id, int AtendimentoCirurgicoId, int PacienteId, string Paciente, int? ConvenioId,
    string? NumeroGuia, string? NumeroLote, DateTime Competencia, DateTime? DataEnvio, DateTime? DataRetorno,
    decimal ValorApresentado, decimal ValorGlosado, decimal ValorGlosaRecuperada, decimal ValorReconhecido,
    FaturamentoStatus Status, string? Observacao, DateTime DataCadastro, DateTime? DataAtualizacao,
    byte[] RowVersion, List<FaturamentoItemDto> Itens, List<GlosaDto> Glosas);
public record RecebimentoDto(int Id, DateTime DataRecebimento, decimal ValorRecebido,
    FormaRecebimento FormaRecebimento, string? ReferenciaBancaria, string? DocumentoComprovante,
    bool Estornado, DateTime? DataEstorno, string? MotivoEstorno);
public record ContaReceberDto(int Id, int FaturamentoId, int PacienteId, string Paciente, int? ConvenioId,
    string NumeroDocumento, string Descricao, DateTime Competencia, DateTime DataEmissao, DateTime DataVencimento,
    decimal ValorOriginal, decimal ValorAjustado, decimal ValorRecebido, decimal SaldoAberto,
    ContaReceberStatus Status, string? Observacao, DateTime DataCadastro, DateTime? DataAtualizacao,
    byte[] RowVersion, List<RecebimentoDto> Recebimentos);
public record ConvenioProcedimentoPrecoDto(int Id, int ConvenioId, string CbhpmCodigo, decimal ValorNegociado,
    decimal PercentualPrincipal, decimal PercentualAuxiliar1, decimal PercentualAuxiliar2,
    DateTime VigenciaInicio, DateTime? VigenciaFinal, bool Ativo);
