using System.Text.Json;
using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Cbhpm;
using HemodinksAPI.Api.Features.Cbhpm.Commands;
using HemodinksAPI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Seeders;

public class CbhpmSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _context;
    private readonly ICbhpmCache _cbhpmCache;
    private readonly ILogger<CbhpmSeeder> _logger;

    public CbhpmSeeder(AppDbContext context, ICbhpmCache cbhpmCache, ILogger<CbhpmSeeder> logger)
    {
        _context = context;
        _cbhpmCache = cbhpmCache;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seedFilePath = Path.Combine(AppContext.BaseDirectory, "Data", "SeedData", "cbhpm-geral.json");
        if (!File.Exists(seedFilePath))
        {
            _logger.LogWarning("Arquivo de seed CBHPM nao encontrado em {SeedFilePath}", seedFilePath);
            return;
        }

        await using var stream = File.OpenRead(seedFilePath);
        var payload = await JsonSerializer.DeserializeAsync<CbhpmSeedPayload>(stream, JsonOptions, cancellationToken);
        var items = CbhpmImportRules.Normalize(payload?.Items ?? []);

        if (items.Count == 0)
        {
            _logger.LogWarning("Arquivo de seed CBHPM nao possui procedimentos validos");
            return;
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
                if (!HasChanges(existingItem, item))
                {
                    continue;
                }

                existingItem.Procedimento = item.Procedimento;
                existingItem.Porte = item.Porte;
                existingItem.CustoOperacional = item.CustoOperacional;
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
                Capitulo = item.Capitulo,
                Grupo = item.Grupo,
                PaginaPdf = item.PaginaPdf
            });
            insertedItems++;
        }

        if (insertedItems == 0 && updatedItems == 0)
        {
            _logger.LogInformation("Seed CBHPM ja esta atualizado com {Count} procedimentos", items.Count);
            return;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _cbhpmCache.Invalidate();
        _logger.LogInformation(
            "Seed CBHPM concluido: {InsertedItems} inseridos, {UpdatedItems} atualizados, {TotalItems} no arquivo",
            insertedItems,
            updatedItems,
            items.Count);
    }

    private static bool HasChanges(CbhpmGeral existingItem, CbhpmImportItemDto item)
    {
        return existingItem.Procedimento != item.Procedimento
            || existingItem.Porte != item.Porte
            || existingItem.CustoOperacional != item.CustoOperacional
            || existingItem.Capitulo != item.Capitulo
            || existingItem.Grupo != item.Grupo
            || existingItem.PaginaPdf != item.PaginaPdf;
    }

    private sealed class CbhpmSeedPayload
    {
        public List<CbhpmImportItemDto> Items { get; set; } = [];
    }
}
