using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static async Task<List<ResolvedProcedimento>> ResolveProcedimentosAsync(
        ICbhpmCache cbhpmCache,
        IEnumerable<PacienteProcedimentoCommandDto>? procedimentos,
        string? cbhpmCodigo,
        string? procedimento,
        string? cbhpmPorte,
        CancellationToken cancellationToken)
    {
        var requestedItems = procedimentos?
            .Where(item => item != null)
            .ToList() ?? [];

        if (requestedItems.Count == 0)
        {
            requestedItems =
            [
                new PacienteProcedimentoCommandDto
                {
                    CbhpmCodigo = cbhpmCodigo,
                    CbhpmPorte = cbhpmPorte,
                    Procedimento = procedimento
                }
            ];
        }

        var resolvedItems = new List<ResolvedProcedimento>();

        foreach (var item in requestedItems)
        {
            var resolved = await ResolveProcedimentoItemAsync(cbhpmCache, item, cancellationToken);
            if (resolved == null)
            {
                continue;
            }

            resolvedItems.Add(resolved);
        }

        return resolvedItems;
    }

    public static List<PacienteProcedimento> ToPacienteProcedimentos(IReadOnlyList<ResolvedProcedimento> procedimentos)
    {
        return procedimentos
            .Select((procedimento, index) => new PacienteProcedimento
            {
                CbhpmCodigo = procedimento.Codigo,
                CbhpmPorte = procedimento.Porte,
                Procedimento = procedimento.Nome,
                ValorReferencia = procedimento.ValorReferencia,
                Ordem = index + 1
            })
            .ToList();
    }

    private static async Task<ResolvedProcedimento?> ResolveProcedimentoItemAsync(
        ICbhpmCache cbhpmCache,
        PacienteProcedimentoCommandDto item,
        CancellationToken cancellationToken)
    {
        var codigo = CbhpmCodigoUtils.NormalizeOptional(item.CbhpmCodigo);
        var procedimento = TrimOptional(item.Procedimento);
        var porte = TrimOptional(item.CbhpmPorte);

        if (codigo == null)
        {
            if (procedimento == null)
            {
                return null;
            }

            ValidateManualProcedimento(procedimento, porte);

            return new ResolvedProcedimento(null, procedimento, porte, item.ValorReferencia);
        }

        if (codigo.Length > 20)
        {
            throw new InvalidOperationException("Codigo CBHPM invalido");
        }

        var cbhpm = await cbhpmCache.GetByCodigoAsync(codigo, cancellationToken);

        if (cbhpm != null)
        {
            return new ResolvedProcedimento(CbhpmCodigoUtils.Normalize(cbhpm.Codigo), cbhpm.Procedimento, cbhpm.Porte, cbhpm.ValorReferencia);
        }

        if (procedimento == null)
        {
            throw new InvalidOperationException("Informe a descricao do procedimento para o codigo CBHPM nao cadastrado");
        }

        ValidateManualProcedimento(procedimento, porte);

        return new ResolvedProcedimento(codigo, procedimento, porte, item.ValorReferencia);
    }

    private static void ValidateManualProcedimento(string procedimento, string? porte)
    {
        if (procedimento.Length > 1000)
        {
            throw new InvalidOperationException("Procedimento excede 1000 caracteres");
        }

        if (porte?.Length > 10)
        {
            throw new InvalidOperationException("Porte CBHPM invalido");
        }
    }
}
