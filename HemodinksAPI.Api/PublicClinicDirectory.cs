using System.Text.Json;

namespace HemodinksAPI.Api;

public sealed class PublicClinicDirectory
{
    private const string DefaultRelativeFilePath = "Data/public-clinics.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<PublicClinicDirectoryItem>? _items;

    public PublicClinicDirectory(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configuredPath = configuration["PublicClinicDirectory:FilePath"];
        _filePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, DefaultRelativeFilePath)
            : Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(environment.ContentRootPath, configuredPath);
    }

    public async Task<IReadOnlyList<PublicClinicaEndpointExtensions.PublicClinicaResponse>?> TryListAsync(
        string? search,
        CancellationToken cancellationToken)
    {
        var items = await TryGetSnapshotAsync(cancellationToken);
        if (items == null || items.Count == 0)
        {
            return null;
        }

        var normalizedSearch = search?.Trim();
        var query = items.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(item =>
                item.Nome.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                || item.Slug.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderBy(item => item.Nome, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .Select(item => new PublicClinicaEndpointExtensions.PublicClinicaResponse(
                item.Id,
                item.Nome,
                item.Slug,
                item.TemFoto ? $"/api/public/clinicas/{item.Slug}/foto" : null))
            .ToList();
    }

    public async Task ReplaceAsync(
        IReadOnlyCollection<PublicClinicDirectoryItem> clinics,
        CancellationToken cancellationToken)
    {
        Validate(clinics);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _items = clinics.ToList();
            await SaveAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<PublicClinicDirectoryItem>?> TryGetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!await TryEnsureLoadedAsync(cancellationToken))
            {
                return null;
            }

            return _items!.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TryEnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_items != null)
        {
            return true;
        }

        if (!File.Exists(_filePath))
        {
            return false;
        }

        await using var stream = File.OpenRead(_filePath);
        var items = await JsonSerializer.DeserializeAsync<List<PublicClinicDirectoryItem>>(
            stream,
            SerializerOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("Catalogo publico de clinicas possui JSON invalido.");

        Validate(items);
        _items = items;
        return true;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        Validate(_items!);
        var directoryPath = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Caminho do catalogo publico de clinicas invalido.");
        Directory.CreateDirectory(directoryPath);
        var temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    _items!.OrderBy(item => item.Nome, StringComparer.OrdinalIgnoreCase),
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void Validate(IReadOnlyCollection<PublicClinicDirectoryItem> items)
    {
        foreach (var item in items)
        {
            ValidateItem(item);
        }

        if (items.Select(item => item.Id).Distinct().Count() != items.Count
            || items.Select(item => item.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
        {
            throw new InvalidOperationException("Catalogo publico de clinicas possui IDs ou slugs duplicados.");
        }
    }

    private static void ValidateItem(PublicClinicDirectoryItem item)
    {
        if (item.Id <= 0
            || string.IsNullOrWhiteSpace(item.Nome)
            || string.IsNullOrWhiteSpace(item.Slug))
        {
            throw new InvalidOperationException("Catalogo publico de clinicas possui item incompleto.");
        }
    }
}

public sealed record PublicClinicDirectoryItem(int Id, string Nome, string Slug, bool TemFoto);
