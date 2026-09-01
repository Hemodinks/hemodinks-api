using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;

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
        DateTime? competenciaFinal,
        bool supportsFullTextSearch)
    {
        var canUseGlobalFilters = Perfil.IsAdministradorOuSuper(currentPerfilId)
            || currentPerfilId == Perfil.ControllerId
            || currentPerfilId == Perfil.EquipeId;
        var normalizedMedico = canUseGlobalFilters ? TrimOptional(medico) : null;
        var normalizedConvenio = TrimOptional(convenio);
        var normalizedProcedimento = TrimOptional(procedimento);
        var normalizedCompetenciaInicio = GetMonthStart(competenciaInicio);
        var normalizedCompetenciaFinalExclusive = GetNextMonthStart(competenciaFinal);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var fullTextCondition = FullTextSearchTermBuilder.BuildPrefixCondition(search);
            query = supportsFullTextSearch && fullTextCondition != null
                ? ApplyFullTextSearch(query, search, digits, fullTextCondition)
                : ApplyFallbackSearch(query, search, digits);
        }

        if (!string.IsNullOrWhiteSpace(normalizedMedico))
        {
            var condition = FullTextSearchTermBuilder.BuildPrefixCondition(normalizedMedico);
            query = supportsFullTextSearch && condition != null
                ? query.Where(p =>
                    (p.MedicoUser != null && FullTextSearch.Contains(p.MedicoUser.Nome, condition))
                    || (p.Medico != null && FullTextSearch.Contains(p.Medico, condition)))
                : query.Where(p =>
                    (p.MedicoUser != null && p.MedicoUser.Nome.Contains(normalizedMedico))
                    || (p.Medico != null && p.Medico.Contains(normalizedMedico)));
        }

        if (!string.IsNullOrWhiteSpace(normalizedConvenio))
        {
            var condition = FullTextSearchTermBuilder.BuildPrefixCondition(normalizedConvenio);
            query = supportsFullTextSearch && condition != null
                ? query.Where(p =>
                    (p.ConvenioReferencia != null && FullTextSearch.Contains(p.ConvenioReferencia.DescricaoConvenio, condition))
                    || (p.Convenio != null && FullTextSearch.Contains(p.Convenio, condition)))
                : query.Where(p =>
                    (p.ConvenioReferencia != null && p.ConvenioReferencia.DescricaoConvenio.Contains(normalizedConvenio))
                    || (p.Convenio != null && p.Convenio.Contains(normalizedConvenio)));
        }

        if (!string.IsNullOrWhiteSpace(normalizedProcedimento))
        {
            var condition = FullTextSearchTermBuilder.BuildPrefixCondition(normalizedProcedimento);
            query = supportsFullTextSearch && condition != null
                ? query.Where(p =>
                    (p.Procedimento != null && FullTextSearch.Contains(p.Procedimento, condition))
                    || p.Procedimentos.Any(item => FullTextSearch.Contains(item.Procedimento, condition)))
                : query.Where(p =>
                    (p.Procedimento != null && p.Procedimento.Contains(normalizedProcedimento))
                    || p.Procedimentos.Any(item => item.Procedimento.Contains(normalizedProcedimento)));
        }

        if (normalizedCompetenciaInicio.HasValue || normalizedCompetenciaFinalExclusive.HasValue)
        {
            query = ApplyCompetenciaFilter(
                query,
                normalizedCompetenciaInicio,
                normalizedCompetenciaFinalExclusive);
        }

        return query;
    }

    private static IQueryable<Paciente> ApplyFullTextSearch(
        IQueryable<Paciente> query,
        string search,
        string digits,
        string condition)
    {
        return query.Where(p =>
            FullTextSearch.Contains(p.NomePaciente, condition)
            || (p.HospitalReferencia != null && FullTextSearch.Contains(p.HospitalReferencia.Nome, condition))
            || (p.Hospital != null && FullTextSearch.Contains(p.Hospital, condition))
            || (p.MedicoUser != null && FullTextSearch.Contains(p.MedicoUser.Nome, condition))
            || (p.Medico != null && FullTextSearch.Contains(p.Medico, condition))
            || (p.ConvenioReferencia != null && FullTextSearch.Contains(p.ConvenioReferencia.DescricaoConvenio, condition))
            || (p.Convenio != null && FullTextSearch.Contains(p.Convenio, condition))
            || (p.OpmeFornecedorReferencia != null && FullTextSearch.Contains(p.OpmeFornecedorReferencia.Fornecedor, condition))
            || (p.OpmeFornecedor != null && FullTextSearch.Contains(p.OpmeFornecedor, condition))
            || (p.Procedimento != null && FullTextSearch.Contains(p.Procedimento, condition))
            || p.User.Email.Contains(search)
            || (p.CbhpmCodigo != null && p.CbhpmCodigo.Contains(search))
            || (p.Autorizacao != null && p.Autorizacao.Contains(search))
            || (p.FaturamentoMedico != null
                && ((p.FaturamentoMedico.GuiaAutorizacaoConvenio != null && p.FaturamentoMedico.GuiaAutorizacaoConvenio.Contains(search))
                    || (p.FaturamentoMedico.CodigoTussCbhpmAmb != null && p.FaturamentoMedico.CodigoTussCbhpmAmb.Contains(search))
                    || (p.FaturamentoMedico.GlosaStatus != null && p.FaturamentoMedico.GlosaStatus.Contains(search))))
            || (!string.IsNullOrEmpty(digits) && p.CbhpmCodigo != null
                && p.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
            || p.Procedimentos.Any(item =>
                FullTextSearch.Contains(item.Procedimento, condition)
                || (item.CbhpmCodigo != null && item.CbhpmCodigo.Contains(search))
                || (!string.IsNullOrEmpty(digits) && item.CbhpmCodigo != null
                    && item.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
                || (item.CbhpmPorte != null && item.CbhpmPorte.Contains(search))));
    }

    private static IQueryable<Paciente> ApplyFallbackSearch(
        IQueryable<Paciente> query,
        string search,
        string digits)
    {
        return query.Where(p =>
            p.NomePaciente.Contains(search)
            || (p.HospitalReferencia != null && p.HospitalReferencia.Nome.Contains(search))
            || (p.Hospital != null && p.Hospital.Contains(search))
            || (p.MedicoUser != null && p.MedicoUser.Nome.Contains(search))
            || (p.Medico != null && p.Medico.Contains(search))
            || (p.ConvenioReferencia != null && p.ConvenioReferencia.DescricaoConvenio.Contains(search))
            || (p.Convenio != null && p.Convenio.Contains(search))
            || (p.OpmeFornecedorReferencia != null && p.OpmeFornecedorReferencia.Fornecedor.Contains(search))
            || (p.OpmeFornecedor != null && p.OpmeFornecedor.Contains(search))
            || (p.Procedimento != null && p.Procedimento.Contains(search))
            || p.User.Email.Contains(search)
            || (p.CbhpmCodigo != null && p.CbhpmCodigo.Contains(search))
            || (p.Autorizacao != null && p.Autorizacao.Contains(search))
            || (p.FaturamentoMedico != null
                && ((p.FaturamentoMedico.GuiaAutorizacaoConvenio != null && p.FaturamentoMedico.GuiaAutorizacaoConvenio.Contains(search))
                    || (p.FaturamentoMedico.CodigoTussCbhpmAmb != null && p.FaturamentoMedico.CodigoTussCbhpmAmb.Contains(search))
                    || (p.FaturamentoMedico.GlosaStatus != null && p.FaturamentoMedico.GlosaStatus.Contains(search))))
            || (!string.IsNullOrEmpty(digits) && p.CbhpmCodigo != null
                && p.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
            || p.Procedimentos.Any(item =>
                item.Procedimento.Contains(search)
                || (item.CbhpmCodigo != null && item.CbhpmCodigo.Contains(search))
                || (!string.IsNullOrEmpty(digits) && item.CbhpmCodigo != null
                    && item.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
                || (item.CbhpmPorte != null && item.CbhpmPorte.Contains(search))));
    }

    private static IQueryable<Paciente> ApplyCompetenciaFilter(
        IQueryable<Paciente> query,
        DateTime? competenciaInicio,
        DateTime? competenciaFinalExclusive)
    {
        return query.Where(p =>
            (p.FaturamentoMedico != null
                && (!competenciaInicio.HasValue || p.FaturamentoMedico.DataCadastro >= competenciaInicio.Value)
                && (!competenciaFinalExclusive.HasValue || p.FaturamentoMedico.DataCadastro < competenciaFinalExclusive.Value))
            || (p.FaturamentoMedico == null
                && (!competenciaInicio.HasValue || p.User.DataCadastro >= competenciaInicio.Value)
                && (!competenciaFinalExclusive.HasValue || p.User.DataCadastro < competenciaFinalExclusive.Value)));
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
