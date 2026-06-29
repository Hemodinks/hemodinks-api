using HemodinksAPI.Application.Features.Common;
using MediatR;

namespace HemodinksAPI.Application.Features.Cbhpm.Queries;

public class GetCbhpmGeralQueryHandler : IRequestHandler<GetCbhpmGeralQuery, PagedResult<CbhpmGeralDto>>
{
    private readonly ICbhpmCache _cbhpmCache;
    private readonly ILogger<GetCbhpmGeralQueryHandler> _logger;

    public GetCbhpmGeralQueryHandler(ICbhpmCache cbhpmCache, ILogger<GetCbhpmGeralQueryHandler> logger)
    {
        _cbhpmCache = cbhpmCache;
        _logger = logger;
    }

    public async Task<PagedResult<CbhpmGeralDto>> Handle(GetCbhpmGeralQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);

            var snapshot = await _cbhpmCache.GetSnapshotAsync(cancellationToken);
            IEnumerable<CbhpmCacheItem> query = snapshot.Items;

            var codigo = CbhpmQueryRules.TrimOptional(request.Codigo);
            if (codigo != null)
            {
                query = query.Where(item => CbhpmCodigoUtils.ContainsNormalizedOrOriginal(item.Codigo, codigo));
            }

            var procedimento = CbhpmQueryRules.TrimOptional(request.Procedimento);
            if (procedimento != null)
            {
                query = query.Where(item =>
                    item.Procedimento.Contains(procedimento, StringComparison.OrdinalIgnoreCase)
                    || (item.Grupo != null && item.Grupo.Contains(procedimento, StringComparison.OrdinalIgnoreCase)));
            }

            var porte = CbhpmQueryRules.TrimOptional(request.Porte);
            if (porte != null)
            {
                query = query.Where(item => string.Equals(item.Porte, porte, StringComparison.OrdinalIgnoreCase));
            }

            var search = CbhpmQueryRules.TrimOptional(request.Search);
            if (search != null)
            {
                query = query.Where(item =>
                    CbhpmCodigoUtils.ContainsNormalizedOrOriginal(item.Codigo, search)
                    || item.Procedimento.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (item.Porte != null && item.Porte.Contains(search, StringComparison.OrdinalIgnoreCase))
                    || (item.Grupo != null && item.Grupo.Contains(search, StringComparison.OrdinalIgnoreCase)));
            }

            var filteredItems = query.ToList();
            filteredItems = ApplyOrdering(filteredItems, request.SortBy, request.SortDirection);
            var totalItems = filteredItems.Count;

            var items = filteredItems
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(item => new CbhpmGeralDto
                {
                    Id = item.Id,
                    Codigo = CbhpmCodigoUtils.Normalize(item.Codigo),
                    Procedimento = item.Procedimento,
                    Porte = item.Porte,
                    CustoOperacional = item.CustoOperacional,
                    ValorReferencia = item.ValorReferencia,
                    Capitulo = item.Capitulo,
                    Grupo = item.Grupo
                })
                .ToList();

            return new PagedResult<CbhpmGeralDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar procedimentos CBHPM");
            throw;
        }
    }

    private static List<CbhpmCacheItem> ApplyOrdering(List<CbhpmCacheItem> items, string? sortBy, string? sortDirection)
    {
        var normalizedSortBy = NormalizeSortBy(sortBy);
        var isDescending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        IEnumerable<CbhpmCacheItem> ordered = normalizedSortBy switch
        {
            "procedimento" => isDescending
                ? items.OrderByDescending(item => item.Procedimento, StringComparer.OrdinalIgnoreCase).ThenByDescending(item => item.Id)
                : items.OrderBy(item => item.Procedimento, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id),
            "porte" => isDescending
                ? items.OrderByDescending(item => item.Porte, StringComparer.OrdinalIgnoreCase).ThenByDescending(item => item.Id)
                : items.OrderBy(item => item.Porte, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id),
            "valorreferencia" => isDescending
                ? items.OrderByDescending(item => item.ValorReferencia ?? decimal.MinValue).ThenByDescending(item => item.Codigo).ThenByDescending(item => item.Id)
                : items.OrderBy(item => item.ValorReferencia ?? decimal.MinValue).ThenBy(item => item.Codigo).ThenBy(item => item.Id),
            _ => isDescending
                ? items.OrderByDescending(item => item.Codigo, StringComparer.OrdinalIgnoreCase).ThenByDescending(item => item.Id)
                : items.OrderBy(item => item.Codigo, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id),
        };

        return ordered.ToList();
    }

    private static string NormalizeSortBy(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? "codigo"
            : sortBy.Trim().ToLowerInvariant();
    }
}

internal static class CbhpmQueryRules
{
    public static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
