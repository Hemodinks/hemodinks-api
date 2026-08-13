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
            ? Path.Combine(environment.ContentRootPath, "Data", "public-clinics.json")
            : Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(environment.ContentRootPath, configuredPath);
    }

    public async Task<IReadOnlyList<PublicClinicaEndpointExtensions.PublicClinicaResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken)
    {
        var items = await GetSnapshotAsync(cancellationToken);
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

    public async Task UpsertAsync(
        PublicClinicDirectoryItem clinic,
        CancellationToken cancellationToken)
    {
        ValidateItem(clinic);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            _items!.RemoveAll(item => item.Id == clinic.Id
                || item.Slug.Equals(clinic.Slug, StringComparison.OrdinalIgnoreCase));
            _items.Add(clinic);
            await SaveAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(int clinicId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_items!.RemoveAll(item => item.Id == clinicId) > 0)
            {
                await SaveAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<PublicClinicDirectoryItem>> GetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _items!.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_items != null)
        {
            return;
        }

        if (!File.Exists(_filePath))
        {
            throw new InvalidOperationException(
                $"Catalogo publico de clinicas nao encontrado em {DefaultRelativeFilePath}.");
        }

        await using var stream = File.OpenRead(_filePath);
        var items = await JsonSerializer.DeserializeAsync<List<PublicClinicDirectoryItem>>(
            stream,
            SerializerOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("Catalogo publico de clinicas possui JSON invalido.");

        Validate(items);
        _items = items;
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
