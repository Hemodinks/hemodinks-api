using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Cbhpm.Commands;

public class ImportCbhpmGeralCommandHandler : IRequestHandler<ImportCbhpmGeralCommand, CbhpmImportResultDto>
{
    private readonly IAppDbContext _context;
    private readonly ICbhpmCache _cbhpmCache;
    private readonly ILogger<ImportCbhpmGeralCommandHandler> _logger;

    public ImportCbhpmGeralCommandHandler(
        IAppDbContext context,
        ICbhpmCache cbhpmCache,
        ILogger<ImportCbhpmGeralCommandHandler> logger)
    {
        _context = context;
        _cbhpmCache = cbhpmCache;
        _logger = logger;
    }

    public async Task<CbhpmImportResultDto> Handle(ImportCbhpmGeralCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var items = CbhpmImportRules.Normalize(request.Items);
            if (items.Count == 0)
            {
                throw new InvalidOperationException("Nenhum procedimento CBHPM informado para importacao");
            }

            var codigos = items.Select(item => item.Codigo).ToList();
            var existingItems = await _context.CbhpmGeral
                .Where(item => codigos.Contains(item.Codigo))
                .ToDictionaryAsync(item => item.Codigo, cancellationToken);

            var insertedItems = 0;
            var updatedItems = 0;

            foreach (var item in items)
            {
                if (existingItems.TryGetValue(item.Codigo, out var existingItem))
                {
                    existingItem.Procedimento = item.Procedimento;
                    existingItem.Porte = item.Porte;
                    existingItem.CustoOperacional = item.CustoOperacional;
                    existingItem.ValorReferencia = item.ValorReferencia;
                    existingItem.Capitulo = item.Capitulo;
                    existingItem.Grupo = item.Grupo;
                    existingItem.PaginaPdf = item.PaginaPdf;
                    updatedItems++;
                    continue;
                }

                _context.CbhpmGeral.Add(new CbhpmGeral
                {
                    Codigo = item.Codigo,
                    Procedimento = item.Procedimento,
                    Porte = item.Porte,
                    CustoOperacional = item.CustoOperacional,
                    ValorReferencia = item.ValorReferencia,
                    Capitulo = item.Capitulo,
                    Grupo = item.Grupo,
                    PaginaPdf = item.PaginaPdf
                });
                insertedItems++;
            }

            await _context.SaveChangesAsync(cancellationToken);
            _cbhpmCache.Invalidate();

            return new CbhpmImportResultDto
            {
                TotalItems = items.Count,
                InsertedItems = insertedItems,
                UpdatedItems = updatedItems
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao importar procedimentos CBHPM");
            throw;
        }
    }
}

public static class CbhpmImportRules
{
    public static List<CbhpmImportItemDto> Normalize(IEnumerable<CbhpmImportItemDto> items)
    {
        return items
            .Select(NormalizeItem)
            .Where(item => item != null)
            .Cast<CbhpmImportItemDto>()
            .GroupBy(item => item.Codigo)
            .Select(group => group.Last())
            .ToList();
    }

    private static CbhpmImportItemDto? NormalizeItem(CbhpmImportItemDto item)
    {
        var codigo = Trim(item.Codigo);
        var procedimento = Trim(item.Procedimento);

        if (codigo == null || procedimento == null)
        {
            return null;
        }

        if (codigo.Length > 20)
        {
            throw new InvalidOperationException($"Codigo CBHPM invalido: {codigo}");
        }

        if (procedimento.Length > 1000)
        {
            throw new InvalidOperationException($"Procedimento CBHPM excede 1000 caracteres: {codigo}");
        }

        var porte = Trim(item.Porte);
        if (porte?.Length > 10)
        {
            throw new InvalidOperationException($"Porte CBHPM invalido para o codigo {codigo}");
        }

        return new CbhpmImportItemDto
        {
            Codigo = codigo,
            Procedimento = procedimento,
            Porte = porte,
            CustoOperacional = item.CustoOperacional,
            ValorReferencia = item.ValorReferencia ?? CbhpmValorReferencia.Calcular(porte, item.CustoOperacional),
            Capitulo = TrimMax(item.Capitulo, 255),
            Grupo = TrimMax(item.Grupo, 255),
            PaginaPdf = item.PaginaPdf
        };
    }

    private static string? Trim(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? TrimMax(string? value, int maxLength)
    {
        var trimmed = Trim(value);
        return trimmed == null || trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }
}
