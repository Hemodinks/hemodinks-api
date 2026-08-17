using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

internal static class PacienteQueryOrdering
{
    public static IQueryable<Paciente> ApplyOrdering(IQueryable<Paciente> query, string? sortBy, string? sortDirection)
    {
        var normalizedSortBy = NormalizeSortBy(sortBy);
        var isDescending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        return normalizedSortBy switch
        {
            "data" or "dataprocedimento" => isDescending
                ? query.OrderByDescending(paciente => paciente.Data).ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.Data).ThenBy(paciente => paciente.Id),
            "dataatendimento" => isDescending
                ? query.OrderByDescending(paciente => paciente.DataAtendimento).ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.DataAtendimento).ThenBy(paciente => paciente.Id),
            "nome" => isDescending
                ? query.OrderByDescending(paciente => paciente.NomePaciente).ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.NomePaciente).ThenBy(paciente => paciente.Id),
            "hospital" => isDescending
                ? query.OrderByDescending(paciente => paciente.HospitalReferencia != null ? paciente.HospitalReferencia.Nome : paciente.Hospital)
                    .ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.HospitalReferencia != null ? paciente.HospitalReferencia.Nome : paciente.Hospital)
                    .ThenBy(paciente => paciente.Id),
            "medico" => isDescending
                ? query.OrderByDescending(paciente => paciente.MedicoUser != null ? paciente.MedicoUser.Nome : paciente.Medico)
                    .ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.MedicoUser != null ? paciente.MedicoUser.Nome : paciente.Medico)
                    .ThenBy(paciente => paciente.Id),
            "convenio" => isDescending
                ? query.OrderByDescending(paciente => paciente.ConvenioReferencia != null ? paciente.ConvenioReferencia.DescricaoConvenio : paciente.Convenio)
                    .ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.ConvenioReferencia != null ? paciente.ConvenioReferencia.DescricaoConvenio : paciente.Convenio)
                    .ThenBy(paciente => paciente.Id),
            "auxiliares" => isDescending
                ? query.OrderByDescending(paciente =>
                        (paciente.MedicoAuxiliar1User != null ? paciente.MedicoAuxiliar1User.Nome : paciente.MedicoAuxiliar1) + " / "
                        + (paciente.MedicoAuxiliar2User != null ? paciente.MedicoAuxiliar2User.Nome : paciente.MedicoAuxiliar2))
                    .ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente =>
                        (paciente.MedicoAuxiliar1User != null ? paciente.MedicoAuxiliar1User.Nome : paciente.MedicoAuxiliar1) + " / "
                        + (paciente.MedicoAuxiliar2User != null ? paciente.MedicoAuxiliar2User.Nome : paciente.MedicoAuxiliar2))
                    .ThenBy(paciente => paciente.Id),
            "status" => isDescending
                ? query.OrderByDescending(paciente => paciente.StatusPago).ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.StatusPago).ThenBy(paciente => paciente.Id),
            "arquivos" => isDescending
                ? query.OrderByDescending(paciente => paciente.Arquivos.Count).ThenByDescending(paciente => paciente.NomePaciente).ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.Arquivos.Count).ThenBy(paciente => paciente.NomePaciente).ThenBy(paciente => paciente.Id),
            _ => isDescending
                ? query.OrderByDescending(paciente => paciente.User.DataAtualizacao ?? paciente.User.DataCadastro).ThenBy(paciente => paciente.NomePaciente).ThenBy(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.User.DataAtualizacao ?? paciente.User.DataCadastro).ThenBy(paciente => paciente.NomePaciente).ThenBy(paciente => paciente.Id),
        };
    }

    public static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeSortBy(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? "data"
            : sortBy.Trim().ToLowerInvariant();
    }
}
