using HemodinksAPI.Domain.Models;

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
        DateTime? dataFinal)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p =>
                p.NomePaciente.Contains(search)
                || (p.Diagnostico != null && p.Diagnostico.Contains(search))
                || p.User.Email.Contains(search)
                || p.User.Telefone.Contains(search)
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

        if (!string.IsNullOrWhiteSpace(medico))
        {
            query = query.Where(p =>
                (p.MedicoUser != null && p.MedicoUser.Nome.Contains(medico))
                || (p.Medico != null && p.Medico.Contains(medico)));
        }

        if (!string.IsNullOrWhiteSpace(convenio))
        {
            query = query.Where(p =>
                (p.ConvenioReferencia != null && p.ConvenioReferencia.DescricaoConvenio.Contains(convenio))
                || (p.Convenio != null && p.Convenio.Contains(convenio)));
        }

        if (!string.IsNullOrWhiteSpace(procedimento))
        {
            query = query.Where(p =>
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
}
