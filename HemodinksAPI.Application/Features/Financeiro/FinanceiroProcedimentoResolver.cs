using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Cbhpm;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

internal static class FinanceiroProcedimentoResolver
{
    public static async Task<ProcedimentoFinanceiroResolvido> ResolveAsync(
        IAppDbContext db,
        AtendimentoProcedimentoInput input,
        int? convenioId,
        DateTime dataProcedimento,
        CancellationToken cancellationToken)
    {
        var codigoNormalizado = CbhpmCodigoUtils.NormalizeOptional(input.CbhpmCodigo);
        if (codigoNormalizado == null)
        {
            return new(
                null,
                input.CbhpmPorte?.Trim().ToUpperInvariant(),
                input.Descricao?.Trim(),
                null,
                null);
        }

        var referencia = await db.CbhpmGeral
            .AsNoTracking()
            .Where(item => item.Codigo
                .Replace(".", "")
                .Replace("-", "")
                .Replace("/", "")
                .Replace(" ", "") == codigoNormalizado)
            .OrderByDescending(item => item.Codigo == input.CbhpmCodigo!.Trim())
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var valorNegociado = convenioId == null
            ? null
            : await db.ConvenioProcedimentoPrecos
                .AsNoTracking()
                .Where(item =>
                    item.ConvenioId == convenioId
                    && item.CbhpmCodigo
                        .Replace(".", "")
                        .Replace("-", "")
                        .Replace("/", "")
                        .Replace(" ", "") == codigoNormalizado
                    && item.Ativo
                    && item.VigenciaInicio <= dataProcedimento
                    && (item.VigenciaFinal == null || item.VigenciaFinal >= dataProcedimento))
                .OrderByDescending(item => item.VigenciaInicio)
                .ThenByDescending(item => item.Id)
                .Select(item => (decimal?)item.ValorNegociado)
                .FirstOrDefaultAsync(cancellationToken);

        return new(
            referencia == null ? codigoNormalizado : CbhpmCodigoUtils.Normalize(referencia.Codigo),
            referencia?.Porte ?? input.CbhpmPorte?.Trim().ToUpperInvariant(),
            referencia?.Procedimento ?? input.Descricao?.Trim(),
            referencia?.ValorReferencia,
            valorNegociado);
    }
}

internal sealed record ProcedimentoFinanceiroResolvido(
    string? Codigo,
    string? Porte,
    string? Descricao,
    decimal? ValorReferencia,
    decimal? ValorNegociado);
