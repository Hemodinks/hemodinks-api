using HemodinksAPI.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HemodinksAPI.Api.Features.Cbhpm;

public interface ICbhpmCache
{
    Task<CbhpmCacheSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<CbhpmCacheItem?> GetByCodigoAsync(string codigo, CancellationToken cancellationToken);
    void Invalidate();
}

public sealed class CbhpmCache : ICbhpmCache
{
    private const string CacheKey = "cbhpm-geral:v1";

    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CbhpmCache> _logger;

    public CbhpmCache(AppDbContext context, IMemoryCache cache, ILogger<CbhpmCache> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CbhpmCacheSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            entry.SlidingExpiration = TimeSpan.FromHours(2);
            entry.Priority = CacheItemPriority.High;

            var items = await _context.CbhpmGeral
                .AsNoTracking()
                .OrderBy(item => item.Codigo)
                .ThenBy(item => item.Id)
                .Select(item => new CbhpmCacheItem(
                    item.Id,
                    item.Codigo,
                    item.Procedimento,
                    item.Porte,
                    item.CustoOperacional,
                    item.Capitulo,
                    item.Grupo,
                    item.PaginaPdf))
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Cache CBHPM carregado com {Count} procedimentos", items.Count);
            return CbhpmCacheSnapshot.Create(items);
        });

        return snapshot ?? CbhpmCacheSnapshot.Empty;
    }

    public async Task<CbhpmCacheItem?> GetByCodigoAsync(string codigo, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot.ByCodigo.TryGetValue(codigo, out var item) ? item : null;
    }

    public void Invalidate()
    {
        _cache.Remove(CacheKey);
    }
}

public sealed record CbhpmCacheItem(
    int Id,
    string Codigo,
    string Procedimento,
    string? Porte,
    decimal? CustoOperacional,
    string? Capitulo,
    string? Grupo,
    int? PaginaPdf);

public sealed class CbhpmCacheSnapshot
{
    public static readonly CbhpmCacheSnapshot Empty = Create([]);

    private CbhpmCacheSnapshot(
        IReadOnlyList<CbhpmCacheItem> items,
        IReadOnlyDictionary<string, CbhpmCacheItem> byCodigo)
    {
        Items = items;
        ByCodigo = byCodigo;
    }

    public IReadOnlyList<CbhpmCacheItem> Items { get; }
    public IReadOnlyDictionary<string, CbhpmCacheItem> ByCodigo { get; }

    public static CbhpmCacheSnapshot Create(IReadOnlyList<CbhpmCacheItem> items)
    {
        return new CbhpmCacheSnapshot(
            items,
            items
                .GroupBy(item => item.Codigo, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase));
    }
}
