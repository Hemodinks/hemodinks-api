using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Features.Common;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

internal static class PacienteFilters
{
    public static IQueryable<Paciente> ApplyFilters(
        IQueryable<Paciente> query,
        string? search,
        string digits,
        string? medico,
        string? convenio,
        string? procedimento,
        int[] medicoUserIds,
        int[] convenioIds,
        DateTime? dataInicio,
        DateTime? dataFinal,
        bool supportsFullTextSearch)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var fullTextCondition = FullTextSearchTermBuilder.BuildPrefixCondition(search);
            query = supportsFullTextSearch && fullTextCondition != null
                ? ApplyFullTextSearch(query, search, digits, fullTextCondition)
                : ApplyFallbackSearch(query, search, digits);
        }

        if (!string.IsNullOrWhiteSpace(medico))
        {
            var condition = FullTextSearchTermBuilder.BuildPrefixCondition(medico);
            query = supportsFullTextSearch && condition != null
                ? query.Where(p =>
                    (p.MedicoUser != null && EF.Functions.Contains(p.MedicoUser.Nome, condition))
                    || (p.Medico != null && EF.Functions.Contains(p.Medico, condition)))
                : query.Where(p =>
                    (p.MedicoUser != null && p.MedicoUser.Nome.Contains(medico))
                    || (p.Medico != null && p.Medico.Contains(medico)));
        }

        if (!string.IsNullOrWhiteSpace(convenio))
        {
            var condition = FullTextSearchTermBuilder.BuildPrefixCondition(convenio);
            query = supportsFullTextSearch && condition != null
                ? query.Where(p =>
                    (p.ConvenioReferencia != null && EF.Functions.Contains(p.ConvenioReferencia.DescricaoConvenio, condition))
                    || (p.Convenio != null && EF.Functions.Contains(p.Convenio, condition)))
                : query.Where(p =>
                    (p.ConvenioReferencia != null && p.ConvenioReferencia.DescricaoConvenio.Contains(convenio))
                    || (p.Convenio != null && p.Convenio.Contains(convenio)));
        }

        if (!string.IsNullOrWhiteSpace(procedimento))
        {
            var condition = FullTextSearchTermBuilder.BuildPrefixCondition(procedimento);
            query = supportsFullTextSearch && condition != null
                ? query.Where(p =>
                    (p.Procedimento != null && EF.Functions.Contains(p.Procedimento, condition))
                    || p.Procedimentos.Any(item => EF.Functions.Contains(item.Procedimento, condition)))
                : query.Where(p =>
                    (p.Procedimento != null && p.Procedimento.Contains(procedimento))
                    || p.Procedimentos.Any(item => item.Procedimento.Contains(procedimento)));
        }

        if (medicoUserIds.Length > 0)
        {
            query = query.Where(p => p.MedicoUserId.HasValue && medicoUserIds.Contains(p.MedicoUserId.Value));
        }

        if (convenioIds.Length > 0)
        {
            query = query.Where(p => p.ConvenioId.HasValue && convenioIds.Contains(p.ConvenioId.Value));
        }

        if (dataInicio.HasValue)
        {
            var inicio = dataInicio.Value.Date;
            query = query.Where(p => p.Data.HasValue && p.Data.Value >= inicio);
        }

        if (dataFinal.HasValue)
        {
            var fimExclusivo = dataFinal.Value.Date.AddDays(1);
            query = query.Where(p => p.Data.HasValue && p.Data.Value < fimExclusivo);
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
            EF.Functions.Contains(p.NomePaciente, condition)
            || (p.Diagnostico != null && EF.Functions.Contains(p.Diagnostico, condition))
            || (p.HospitalReferencia != null && EF.Functions.Contains(p.HospitalReferencia.Nome, condition))
            || (p.Hospital != null && EF.Functions.Contains(p.Hospital, condition))
            || (p.MedicoUser != null && EF.Functions.Contains(p.MedicoUser.Nome, condition))
            || (p.Medico != null && EF.Functions.Contains(p.Medico, condition))
            || (p.MedicoAuxiliar1User != null && EF.Functions.Contains(p.MedicoAuxiliar1User.Nome, condition))
            || (p.MedicoAuxiliar1 != null && EF.Functions.Contains(p.MedicoAuxiliar1, condition))
            || (p.MedicoAuxiliar2User != null && EF.Functions.Contains(p.MedicoAuxiliar2User.Nome, condition))
            || (p.MedicoAuxiliar2 != null && EF.Functions.Contains(p.MedicoAuxiliar2, condition))
            || (p.ConvenioReferencia != null && EF.Functions.Contains(p.ConvenioReferencia.DescricaoConvenio, condition))
            || (p.Convenio != null && EF.Functions.Contains(p.Convenio, condition))
            || (p.OpmeFornecedorReferencia != null && EF.Functions.Contains(p.OpmeFornecedorReferencia.Fornecedor, condition))
            || (p.OpmeFornecedor != null && EF.Functions.Contains(p.OpmeFornecedor, condition))
            || (p.Procedimento != null && EF.Functions.Contains(p.Procedimento, condition))
            || p.User.Email.Contains(search)
            || p.User.Telefone.Contains(search)
            || (p.CbhpmCodigo != null && p.CbhpmCodigo.Contains(search))
            || (!string.IsNullOrEmpty(digits)
                && p.CbhpmCodigo != null
                && p.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
            || p.Procedimentos.Any(item =>
                EF.Functions.Contains(item.Procedimento, condition)
                || (item.CbhpmCodigo != null && item.CbhpmCodigo.Contains(search))
                || (!string.IsNullOrEmpty(digits)
                    && item.CbhpmCodigo != null
                    && item.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
                || (item.CbhpmPorte != null && item.CbhpmPorte.Contains(search)))
            || (!string.IsNullOrEmpty(digits) && p.User.Cpf != null && p.User.Cpf.Contains(digits))
            || (!string.IsNullOrEmpty(digits) && p.User.Telefone.Contains(digits)));
    }

    private static IQueryable<Paciente> ApplyFallbackSearch(
        IQueryable<Paciente> query,
        string search,
        string digits)
    {
        return query.Where(p =>
            p.NomePaciente.Contains(search)
            || (p.Diagnostico != null && p.Diagnostico.Contains(search))
            || (p.HospitalReferencia != null && p.HospitalReferencia.Nome.Contains(search))
            || (p.Hospital != null && p.Hospital.Contains(search))
            || (p.MedicoUser != null && p.MedicoUser.Nome.Contains(search))
            || (p.Medico != null && p.Medico.Contains(search))
            || (p.MedicoAuxiliar1User != null && p.MedicoAuxiliar1User.Nome.Contains(search))
            || (p.MedicoAuxiliar1 != null && p.MedicoAuxiliar1.Contains(search))
            || (p.MedicoAuxiliar2User != null && p.MedicoAuxiliar2User.Nome.Contains(search))
            || (p.MedicoAuxiliar2 != null && p.MedicoAuxiliar2.Contains(search))
            || (p.ConvenioReferencia != null && p.ConvenioReferencia.DescricaoConvenio.Contains(search))
            || (p.Convenio != null && p.Convenio.Contains(search))
            || (p.OpmeFornecedorReferencia != null && p.OpmeFornecedorReferencia.Fornecedor.Contains(search))
            || (p.OpmeFornecedor != null && p.OpmeFornecedor.Contains(search))
            || (p.Procedimento != null && p.Procedimento.Contains(search))
            || p.User.Email.Contains(search)
            || p.User.Telefone.Contains(search)
            || (p.CbhpmCodigo != null && p.CbhpmCodigo.Contains(search))
            || (!string.IsNullOrEmpty(digits)
                && p.CbhpmCodigo != null
                && p.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
            || p.Procedimentos.Any(item =>
                item.Procedimento.Contains(search)
                || (item.CbhpmCodigo != null && item.CbhpmCodigo.Contains(search))
                || (!string.IsNullOrEmpty(digits)
                    && item.CbhpmCodigo != null
                    && item.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
                || (item.CbhpmPorte != null && item.CbhpmPorte.Contains(search)))
            || (!string.IsNullOrEmpty(digits) && p.User.Cpf != null && p.User.Cpf.Contains(digits))
            || (!string.IsNullOrEmpty(digits) && p.User.Telefone.Contains(digits)));
    }
}
