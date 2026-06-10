using HemodinksAPI.Application.Features.Cbhpm;
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
                query = query.Where(item => item.Codigo.Contains(codigo, StringComparison.OrdinalIgnoreCase));
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
                    item.Codigo.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || item.Procedimento.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (item.Porte != null && item.Porte.Contains(search, StringComparison.OrdinalIgnoreCase))
                    || (item.Grupo != null && item.Grupo.Contains(search, StringComparison.OrdinalIgnoreCase)));
            }

            var filteredItems = query.ToList();
            var totalItems = filteredItems.Count;

            var items = filteredItems
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(item => new CbhpmGeralDto
                {
                    Id = item.Id,
                    Codigo = item.Codigo,
                    Procedimento = item.Procedimento,
                    Porte = item.Porte,
                    CustoOperacional = item.CustoOperacional,
                    ValorReferencia = item.ValorReferencia,
                    Capitulo = item.Capitulo,
                    Grupo = item.Grupo,
                    PaginaPdf = item.PaginaPdf
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
}

internal static class CbhpmQueryRules
{
    public static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
