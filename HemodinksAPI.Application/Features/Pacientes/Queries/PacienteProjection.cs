using System.Linq.Expressions;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

internal static class PacienteProjection
{
    public static Expression<Func<Paciente, PacienteDto>> ToPacienteDto(int currentUserId)
    {
        return p => new PacienteDto
        {
            Id = p.Id,
            UserId = p.UserId,
            Data = p.Data,
            DataCadastro = p.User.DataCadastro,
            DataAtualizacao = p.User.DataAtualizacao,
            NomePaciente = p.NomePaciente,
            Diagnostico = p.Diagnostico,
            HospitalId = p.HospitalId,
            Hospital = p.HospitalReferencia != null ? p.HospitalReferencia.Nome : p.Hospital,
            MedicoUserId = p.MedicoUserId,
            Medico = p.MedicoUser != null ? p.MedicoUser.Nome : p.Medico,
            MedicoAuxiliar1UserId = p.MedicoAuxiliar1UserId,
            MedicoAuxiliar1 = p.MedicoAuxiliar1User != null ? p.MedicoAuxiliar1User.Nome : p.MedicoAuxiliar1,
            MedicoAuxiliar2UserId = p.MedicoAuxiliar2UserId,
            MedicoAuxiliar2 = p.MedicoAuxiliar2User != null ? p.MedicoAuxiliar2User.Nome : p.MedicoAuxiliar2,
            ConvenioId = p.ConvenioId,
            Convenio = p.ConvenioReferencia != null ? p.ConvenioReferencia.DescricaoConvenio : p.Convenio,
            OpmeFornecedorId = p.OpmeFornecedorId,
            OpmeFornecedor = p.OpmeFornecedorReferencia != null ? p.OpmeFornecedorReferencia.Fornecedor : p.OpmeFornecedor,
            CbhpmCodigo = p.CbhpmCodigo,
            CbhpmPorte = p.CbhpmPorte,
            Procedimento = p.Procedimento,
            Procedimentos = p.Procedimentos
                .OrderBy(item => item.Ordem)
                .ThenBy(item => item.Id)
                .Select(item => new PacienteProcedimentoDto
                {
                    Id = item.Id,
                    CbhpmCodigo = item.CbhpmCodigo,
                    CbhpmPorte = item.CbhpmPorte,
                    Procedimento = item.Procedimento,
                    ValorReferencia = item.ValorReferencia,
                    Ordem = item.Ordem
                })
                .ToList(),
            Autorizacao = p.Autorizacao,
            Pagamento = p.Pagamento,
            RepasseGlosa = p.RepasseGlosa,
            StatusPago = p.StatusPago,
            Cpf = p.User.Cpf,
            Email = p.User.Email,
            Telefone = p.User.Telefone,
            FotoPerfil = p.User.FotoPerfil,
            DataNascimento = p.User.DataNascimento,
            Ativo = p.User.Ativo,
            ArquivosCount = p.Arquivos.Count,
            Faturamento = p.FaturamentoMedico == null ? null : new PacienteFaturamentoDto
            {
                Id = p.FaturamentoMedico.Id,
                PacienteId = p.FaturamentoMedico.PacienteId,
                HonorariosCirurgiao = p.FaturamentoMedico.HonorariosCirurgiao,
                HonorariosAuxiliares = p.FaturamentoMedico.HonorariosAuxiliares,
                HonorariosAnestesista = p.FaturamentoMedico.HonorariosAnestesista,
                AnestesistaFaturadoSeparado = p.FaturamentoMedico.AnestesistaFaturadoSeparado,
                Anestesista = p.FaturamentoMedico.Anestesista,
                CodigoTussCbhpmAmb = p.FaturamentoMedico.CodigoTussCbhpmAmb,
                PorteCirurgicoAnestesico = p.FaturamentoMedico.PorteCirurgicoAnestesico,
                GuiaAutorizacaoConvenio = p.FaturamentoMedico.GuiaAutorizacaoConvenio,
                GuiaInternacaoOuSadt = p.FaturamentoMedico.GuiaInternacaoOuSadt,
                OpmeMateriaisEspeciais = p.FaturamentoMedico.OpmeMateriaisEspeciais,
                TissXmlStatus = p.FaturamentoMedico.TissXmlStatus,
                ValorGlosa = p.FaturamentoMedico.ValorGlosa,
                GlosaStatus = p.FaturamentoMedico.GlosaStatus,
                RecursoGlosa = p.FaturamentoMedico.RecursoGlosa,
                ConferenciaPagamentoRealizada = p.FaturamentoMedico.ConferenciaPagamentoRealizada,
                RepasseMedico = p.FaturamentoMedico.RepasseMedico,
                RepasseMedicoObservacao = p.FaturamentoMedico.RepasseMedicoObservacao,
                TipoFaturamentoParticular = p.FaturamentoMedico.TipoFaturamentoParticular,
                ReciboNotaContrato = p.FaturamentoMedico.ReciboNotaContrato,
                Observacoes = p.FaturamentoMedico.Observacoes,
                DataCadastro = p.FaturamentoMedico.DataCadastro,
                DataAtualizacao = p.FaturamentoMedico.DataAtualizacao,
                CompetenciaInicio = p.FaturamentoMedico.CompetenciaInicio,
                CompetenciaFinal = p.FaturamentoMedico.CompetenciaFinal
            },
            ObservacoesNaoLidasCount = p.Observacoes.Count(observacao =>
                observacao.DestinatarioUserId == currentUserId
                && observacao.DataLeitura == null)
        };
    }
}
