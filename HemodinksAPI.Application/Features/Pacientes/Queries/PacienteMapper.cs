using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

internal static class PacienteMapper
{
    public static PacienteDto ToDto(Paciente paciente)
    {
        var dto = new PacienteDto
        {
            Id = paciente.Id,
            UserId = paciente.UserId,
            Data = paciente.Data,
            DataCadastro = paciente.User.DataCadastro,
            DataAtualizacao = paciente.User.DataAtualizacao,
            NomePaciente = paciente.NomePaciente,
            Diagnostico = paciente.Diagnostico,
            TratamentoMedico = paciente.TratamentoMedico,
            HospitalId = paciente.HospitalId,
            Hospital = paciente.HospitalReferencia?.Nome ?? paciente.Hospital,
            MedicoUserId = paciente.MedicoUserId,
            Medico = paciente.MedicoUser?.Nome ?? paciente.Medico,
            MedicoAuxiliar1UserId = paciente.MedicoAuxiliar1UserId,
            MedicoAuxiliar1 = paciente.MedicoAuxiliar1User?.Nome ?? paciente.MedicoAuxiliar1,
            MedicoAuxiliar2UserId = paciente.MedicoAuxiliar2UserId,
            MedicoAuxiliar2 = paciente.MedicoAuxiliar2User?.Nome ?? paciente.MedicoAuxiliar2,
            ConvenioId = paciente.ConvenioId,
            Convenio = paciente.ConvenioReferencia?.DescricaoConvenio ?? paciente.Convenio,
            OpmeFornecedorId = paciente.OpmeFornecedorId,
            OpmeFornecedor = paciente.OpmeFornecedorReferencia?.Fornecedor ?? paciente.OpmeFornecedor,
            CbhpmCodigo = paciente.CbhpmCodigo,
            CbhpmPorte = paciente.CbhpmPorte,
            Procedimento = paciente.Procedimento,
            Procedimentos = ToProcedimentoDtos(paciente),
            Autorizacao = paciente.Autorizacao,
            Pagamento = paciente.Pagamento,
            RepasseGlosa = paciente.RepasseGlosa,
            StatusPago = paciente.StatusPago,
            Cpf = paciente.User.Cpf,
            Email = paciente.User.Email,
            Telefone = paciente.User.Telefone,
            FotoPerfil = paciente.User.FotoPerfil,
            DataNascimento = paciente.User.DataNascimento,
            Ativo = paciente.User.Ativo,
            ArquivosCount = paciente.Arquivos.Count,
            Faturamento = ToFaturamentoDto(paciente.FaturamentoMedico),
            Arquivos = paciente.Arquivos
                .OrderByDescending(arquivo => arquivo.DataUpload)
                .Select(ToArquivoDto)
                .ToList()
        };

        return NormalizeProcedureCodes(dto);
    }

    public static PacienteDto NormalizeProcedureCodes(PacienteDto paciente)
    {
        paciente.CbhpmCodigo = CbhpmCodigoUtils.NormalizeOptional(paciente.CbhpmCodigo);
        foreach (var procedimento in paciente.Procedimentos)
        {
            procedimento.CbhpmCodigo = CbhpmCodigoUtils.NormalizeOptional(procedimento.CbhpmCodigo);
        }

        return paciente;
    }

    public static PacienteArquivoDto ToArquivoDto(PacienteArquivo arquivo)
    {
        return new PacienteArquivoDto
        {
            Id = arquivo.Id,
            NomeOriginal = arquivo.NomeOriginal,
            ContentType = arquivo.ContentType,
            TamanhoBytes = arquivo.TamanhoBytes,
            Url = arquivo.Url,
            DataUpload = arquivo.DataUpload
        };
    }

    public static PacienteFaturamentoDto? ToFaturamentoDto(FaturamentoMedico? faturamento)
    {
        if (faturamento == null)
        {
            return null;
        }

        return new PacienteFaturamentoDto
        {
            Id = faturamento.Id,
            PacienteId = faturamento.PacienteId,
            HonorariosCirurgiao = faturamento.HonorariosCirurgiao,
            HonorariosAuxiliares = faturamento.HonorariosAuxiliares,
            HonorariosAnestesista = faturamento.HonorariosAnestesista,
            AnestesistaFaturadoSeparado = faturamento.AnestesistaFaturadoSeparado,
            Anestesista = faturamento.Anestesista,
            CodigoTussCbhpmAmb = faturamento.CodigoTussCbhpmAmb,
            PorteCirurgicoAnestesico = faturamento.PorteCirurgicoAnestesico,
            GuiaAutorizacaoConvenio = faturamento.GuiaAutorizacaoConvenio,
            GuiaInternacaoOuSadt = faturamento.GuiaInternacaoOuSadt,
            OpmeMateriaisEspeciais = faturamento.OpmeMateriaisEspeciais,
            TissXmlStatus = faturamento.TissXmlStatus,
            ValorGlosa = faturamento.ValorGlosa,
            GlosaStatus = faturamento.GlosaStatus,
            RecursoGlosa = faturamento.RecursoGlosa,
            ConferenciaPagamentoRealizada = faturamento.ConferenciaPagamentoRealizada,
            RepasseMedico = faturamento.RepasseMedico,
            RepasseMedicoObservacao = faturamento.RepasseMedicoObservacao,
            TipoFaturamentoParticular = faturamento.TipoFaturamentoParticular,
            ReciboNotaContrato = faturamento.ReciboNotaContrato,
            Observacoes = faturamento.Observacoes,
            DataCadastro = faturamento.DataCadastro,
            DataAtualizacao = faturamento.DataAtualizacao
        };
    }

    private static List<PacienteProcedimentoDto> ToProcedimentoDtos(Paciente paciente)
    {
        var procedimentos = paciente.Procedimentos
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
            .ToList();

        if (procedimentos.Count > 0 || string.IsNullOrWhiteSpace(paciente.Procedimento))
        {
            return procedimentos;
        }

        return
        [
            new PacienteProcedimentoDto
            {
                CbhpmCodigo = paciente.CbhpmCodigo,
                CbhpmPorte = paciente.CbhpmPorte,
                Procedimento = paciente.Procedimento,
                Ordem = 1
            }
        ];
    }
}
