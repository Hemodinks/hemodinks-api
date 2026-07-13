using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Faturamentos.Queries;

internal static class FaturamentoMedicoFilters
{
    public static IQueryable<Paciente> ApplyFilters(
        IQueryable<Paciente> query,
        int currentPerfilId,
        string? search,
        string digits,
        string? medico,
        string? convenio,
        string? procedimento,
        DateTime? competenciaInicio,
        DateTime? competenciaFinal)
    {
        var canUseGlobalFilters = currentPerfilId is Perfil.AdministradorId or Perfil.ControllerId;
        var normalizedMedico = canUseGlobalFilters ? TrimOptional(medico) : null;
        var normalizedConvenio = TrimOptional(convenio);
        var normalizedProcedimento = TrimOptional(procedimento);
        var normalizedCompetenciaInicio = GetMonthStart(competenciaInicio);
        var normalizedCompetenciaFinalExclusive = GetNextMonthStart(competenciaFinal);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p =>
                p.NomePaciente.Contains(search)
                || p.User.Email.Contains(search)
                || (p.HospitalReferencia != null && p.HospitalReferencia.Nome.Contains(search))
                || (p.Hospital != null && p.Hospital.Contains(search))
                || (p.MedicoUser != null && p.MedicoUser.Nome.Contains(search))
                || (p.Medico != null && p.Medico.Contains(search))
                || (p.ConvenioReferencia != null && p.ConvenioReferencia.DescricaoConvenio.Contains(search))
                || (p.Convenio != null && p.Convenio.Contains(search))
                || (p.OpmeFornecedorReferencia != null && p.OpmeFornecedorReferencia.Fornecedor.Contains(search))
                || (p.OpmeFornecedor != null && p.OpmeFornecedor.Contains(search))
                || (p.Procedimento != null && p.Procedimento.Contains(search))
                || (p.CbhpmCodigo != null && p.CbhpmCodigo.Contains(search))
                || (p.Autorizacao != null && p.Autorizacao.Contains(search))
                || (p.FaturamentoMedico != null
                    && ((p.FaturamentoMedico.GuiaAutorizacaoConvenio != null && p.FaturamentoMedico.GuiaAutorizacaoConvenio.Contains(search))
                        || (p.FaturamentoMedico.CodigoTussCbhpmAmb != null && p.FaturamentoMedico.CodigoTussCbhpmAmb.Contains(search))
                        || (p.FaturamentoMedico.GlosaStatus != null && p.FaturamentoMedico.GlosaStatus.Contains(search))))
                || (!string.IsNullOrEmpty(digits)
                    && p.CbhpmCodigo != null
                    && p.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
                || p.Procedimentos.Any(item =>
                    item.Procedimento.Contains(search)
                    || (item.CbhpmCodigo != null && item.CbhpmCodigo.Contains(search))
                    || (!string.IsNullOrEmpty(digits)
                        && item.CbhpmCodigo != null
                        && item.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
                    || (item.CbhpmPorte != null && item.CbhpmPorte.Contains(search))));
        }

        if (!string.IsNullOrWhiteSpace(normalizedMedico))
        {
            query = query.Where(p =>
                (p.MedicoUser != null && p.MedicoUser.Nome.Contains(normalizedMedico))
                || (p.Medico != null && p.Medico.Contains(normalizedMedico)));
        }

        if (!string.IsNullOrWhiteSpace(normalizedConvenio))
        {
            query = query.Where(p =>
                (p.ConvenioReferencia != null && p.ConvenioReferencia.DescricaoConvenio.Contains(normalizedConvenio))
                || (p.Convenio != null && p.Convenio.Contains(normalizedConvenio)));
        }

        if (!string.IsNullOrWhiteSpace(normalizedProcedimento))
        {
            query = query.Where(p =>
                (p.Procedimento != null && p.Procedimento.Contains(normalizedProcedimento))
                || p.Procedimentos.Any(item => item.Procedimento.Contains(normalizedProcedimento)));
        }

        if (normalizedCompetenciaInicio.HasValue)
        {
            var competenciaInicioValue = normalizedCompetenciaInicio.Value;
            query = query.Where(p =>
                (p.FaturamentoMedico == null
                    ? p.Data
                    : p.FaturamentoMedico.DataCadastro) >= competenciaInicioValue);
        }

        if (normalizedCompetenciaFinalExclusive.HasValue)
        {
            var competenciaFinalExclusiveValue = normalizedCompetenciaFinalExclusive.Value;
            query = query.Where(p =>
                (p.FaturamentoMedico == null
                    ? p.Data
                    : p.FaturamentoMedico.DataCadastro) < competenciaFinalExclusiveValue);
        }

        return query;
    }

    private static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTime? GetMonthStart(DateTime? value)
    {
        return value.HasValue
            ? new DateTime(value.Value.Year, value.Value.Month, 1)
            : null;
    }

    private static DateTime? GetNextMonthStart(DateTime? value)
    {
        return value.HasValue
            ? new DateTime(value.Value.Year, value.Value.Month, 1).AddMonths(1)
            : null;
    }
}
