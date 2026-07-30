using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Financeiro;

internal static class FinanceiroMapper
{
    public static AtendimentoDto ToDto(AtendimentoCirurgico x) => new(x.Id, x.PacienteId, x.Paciente.NomePaciente,
        x.DataProcedimento, x.HospitalId, x.ConvenioId, x.OpmeFornecedorId, x.OpmeFornecedor?.Fornecedor,
        x.MedicoResponsavelId, x.MedicoAuxiliar1Id,
        x.MedicoAuxiliar2Id, x.Diagnostico, x.TratamentoMedico, x.NumeroAutorizacao,
        x.ValorGlosa, x.MotivoGlosa, x.Observacao, x.Status, x.DataCadastro, x.DataAtualizacao,
        x.Procedimentos.OrderBy(p => p.Ordem).Select(p => new AtendimentoProcedimentoDto(p.Id, p.CbhpmCodigo,
            p.CbhpmPorte, p.Descricao, p.Quantidade, p.PesoPercentual, p.ValorReferencia, p.ValorNegociado, p.Ordem)).ToList(),
        x.Arquivos.OrderByDescending(a => a.DataUpload).Select(a => new AtendimentoArquivoDto(
            a.Id, a.NomeOriginal, a.ContentType, a.TamanhoBytes, a.Url, a.DataUpload)).ToList());

    public static FaturamentoDto ToDto(Faturamento x) => new(x.Id, x.AtendimentoCirurgicoId,
        x.AtendimentoCirurgico.PacienteId, x.AtendimentoCirurgico.Paciente.NomePaciente, x.ConvenioId,
        x.NumeroGuia, x.NumeroLote, x.Competencia, x.DataEnvio, x.DataRetorno, x.ValorApresentado,
        x.ValorGlosado, x.ValorGlosaRecuperada, x.ValorReconhecido, x.Status, x.Observacao,
        x.DataCadastro, x.DataAtualizacao, x.RowVersion,
        x.Itens.OrderBy(i => i.Ordem).Select(i => new FaturamentoItemDto(i.Id, i.AtendimentoProcedimentoId,
            i.Codigo, i.Descricao, i.Quantidade, i.PesoPercentual, i.ValorUnitario, i.ValorApresentado,
            i.ValorGlosado, i.ValorAprovado, i.Status, i.Ordem)).ToList(),
        x.Glosas.Select(g => new GlosaDto(g.Id, g.FaturamentoItemId, g.CodigoMotivo, g.DescricaoMotivo,
            g.ValorGlosado, g.DataGlosa, g.Status, g.Observacao, g.Recursos.OrderByDescending(r => r.DataCadastro)
                .Select(r => new RecursoGlosaDto(r.Id, r.DataEnvio, r.Justificativa, r.ValorRecorrido,
                    r.DataResposta, r.ValorRecuperado, r.Status, r.Observacao)).ToList())).ToList());

    public static ContaReceberDto ToDto(ContaReceber x) => new(x.Id, x.FaturamentoId, x.PacienteId,
        x.Paciente.NomePaciente, x.ConvenioId, x.NumeroDocumento, x.Descricao, x.Competencia, x.DataEmissao,
        x.DataVencimento, x.ValorOriginal, x.ValorAjustado, x.ValorRecebido, x.SaldoAberto, x.Status,
        x.Observacao, x.DataCadastro, x.DataAtualizacao, x.RowVersion,
        x.Recebimentos.OrderByDescending(r => r.DataRecebimento)
            .Select(r => new RecebimentoDto(r.Id, r.DataRecebimento, r.ValorRecebido, r.FormaRecebimento,
                r.ReferenciaBancaria, r.DocumentoComprovante, r.Estornado, r.DataEstorno, r.MotivoEstorno)).ToList());
}
