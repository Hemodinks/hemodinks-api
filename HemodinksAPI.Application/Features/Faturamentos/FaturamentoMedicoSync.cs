using System.Globalization;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Faturamentos;

public static class FaturamentoMedicoSync
{
    public static FaturamentoMedico EnsureSynced(Paciente paciente, DateTime utcNow)
    {
        var faturamento = paciente.FaturamentoMedico ?? new FaturamentoMedico
        {
            ClinicaId = paciente.ClinicaId,
            Paciente = paciente,
            PacienteId = paciente.Id,
            DataCadastro = utcNow
        };

        if (paciente.FaturamentoMedico == null)
        {
            paciente.FaturamentoMedico = faturamento;
        }

        var pagamento = ParseCurrency(paciente.Pagamento);
        var glosa = ParseCurrency(paciente.RepasseGlosa);

        faturamento.HonorariosCirurgiao = pagamento ?? faturamento.HonorariosCirurgiao;
        faturamento.ValorGlosa = glosa ?? faturamento.ValorGlosa;
        faturamento.RepasseMedico = pagamento.HasValue || glosa.HasValue
            ? (pagamento ?? 0m) - (glosa ?? 0m)
            : faturamento.RepasseMedico;
        faturamento.GuiaAutorizacaoConvenio = TrimOrCurrent(paciente.Autorizacao, faturamento.GuiaAutorizacaoConvenio);
        faturamento.OpmeMateriaisEspeciais = ResolveOpmeMateriais(paciente, faturamento.OpmeMateriaisEspeciais);
        faturamento.CodigoTussCbhpmAmb = BuildProcedureCodes(paciente) ?? faturamento.CodigoTussCbhpmAmb;
        faturamento.PorteCirurgicoAnestesico = BuildProcedurePortes(paciente) ?? faturamento.PorteCirurgicoAnestesico;
        faturamento.ConferenciaPagamentoRealizada = paciente.StatusPago;
        faturamento.GlosaStatus = ResolveGlosaStatus(glosa, paciente.RepasseGlosa, paciente.StatusPago);
        faturamento.TipoFaturamentoParticular = ResolveTipoFaturamento(paciente);
        faturamento.DataAtualizacao = utcNow;

        return faturamento;
    }

    public static decimal? ParseCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Trim()
            .Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Replace(".", string.Empty)
            .Replace(",", ".", StringComparison.Ordinal)
            .Where(character => char.IsDigit(character) || character is '.' or '-')
            .Aggregate(string.Empty, (current, character) => current + character);

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }

    private static string ResolveTipoFaturamento(Paciente paciente)
    {
        var convenio = !string.IsNullOrWhiteSpace(paciente.Convenio)
            ? paciente.Convenio
            : paciente.ConvenioReferencia?.DescricaoConvenio;

        if (string.IsNullOrWhiteSpace(convenio))
        {
            return paciente.ConvenioId.HasValue ? "Convenio" : "Particular";
        }

        return convenio.Contains("particular", StringComparison.OrdinalIgnoreCase)
            ? "Particular"
            : "Convenio";
    }

    private static string? ResolveOpmeMateriais(Paciente paciente, string? current)
    {
        var opme = !string.IsNullOrWhiteSpace(paciente.OpmeFornecedor)
            ? paciente.OpmeFornecedor
            : paciente.OpmeFornecedorReferencia?.Fornecedor;

        return TrimOrCurrent(opme, current);
    }

    private static string? TrimOrCurrent(string? value, string? current)
    {
        return string.IsNullOrWhiteSpace(value) ? current : value.Trim();
    }

    private static string? BuildProcedureCodes(Paciente paciente)
    {
        var codes = paciente.Procedimentos
            .OrderBy(item => item.Ordem)
            .ThenBy(item => item.Id)
            .Select(item => item.CbhpmCodigo)
            .Prepend(paciente.CbhpmCodigo)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return codes.Count == 0 ? null : string.Join(", ", codes);
    }

    private static string? BuildProcedurePortes(Paciente paciente)
    {
        var portes = paciente.Procedimentos
            .OrderBy(item => item.Ordem)
            .ThenBy(item => item.Id)
            .Select(item => item.CbhpmPorte)
            .Prepend(paciente.CbhpmPorte)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return portes.Count == 0 ? null : string.Join(", ", portes);
    }

    private static string? ResolveGlosaStatus(decimal? glosa, string? rawGlosa, bool statusPago)
    {
        if (glosa > 0)
        {
            return "Glosa informada";
        }

        if (!string.IsNullOrWhiteSpace(rawGlosa))
        {
            return rawGlosa.Trim();
        }

        return statusPago ? "Pagamento conferido" : null;
    }
}
