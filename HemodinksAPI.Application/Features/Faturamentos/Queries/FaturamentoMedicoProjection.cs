using System.Linq.Expressions;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Faturamentos.Queries;

internal static class FaturamentoMedicoProjection
{
    public static Expression<Func<Paciente, PacienteDto>> ToPacienteDto()
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
            TratamentoMedico = p.TratamentoMedico,
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
            Faturamento = PacienteMapper.ToFaturamentoDto(p.FaturamentoMedico)
        };
    }
}
