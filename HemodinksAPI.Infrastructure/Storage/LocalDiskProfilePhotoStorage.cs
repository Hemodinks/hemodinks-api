using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Storage;

public class LocalDiskProfilePhotoStorage : IProfilePhotoStorage
{
    private static readonly IReadOnlyDictionary<string, string> AllowedContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

    private static readonly IReadOnlyDictionary<string, string> ContentTypesByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    private const string StorageFolder = "profile-photos";

    private readonly ProfilePhotoStorageOptions _options;
    private readonly LocalStorageOptions _localOptions;
    private readonly ILogger<LocalDiskProfilePhotoStorage> _logger;

    public LocalDiskProfilePhotoStorage(
        IOptions<ProfilePhotoStorageOptions> options,
        IOptions<LocalStorageOptions> localOptions,
        ILogger<LocalDiskProfilePhotoStorage> logger)
    {
        _options = options.Value;
        _localOptions = localOptions.Value;
        _logger = logger;
    }

    public async Task<string?> SaveAsync(string? fotoPerfil, string? currentFotoPerfil, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fotoPerfil))
        {
            await DeleteAsync(currentFotoPerfil, cancellationToken);
            return null;
        }

        if (!IsDataUrl(fotoPerfil))
        {
            if (!string.IsNullOrWhiteSpace(currentFotoPerfil)
                && string.Equals(fotoPerfil, currentFotoPerfil, StringComparison.Ordinal))
            {
                return currentFotoPerfil;
            }

            throw new InvalidOperationException("Foto de perfil invalida");
        }

        var parsedPhoto = ParseDataUrl(fotoPerfil);

        if (parsedPhoto.Bytes.Length > _options.MaxBytes)
        {
            throw new InvalidOperationException($"A foto deve ter no maximo {_options.MaxBytes / 1024 / 1024} MB");
        }

        var relativePath = $"users/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{parsedPhoto.Extension}";
        var physicalPath = LocalStoragePathHelper.GetPhysicalPath(_localOptions, StorageFolder, relativePath);
        var directoryPath = Path.GetDirectoryName(physicalPath)
            ?? throw new InvalidOperationException("Nao foi possivel resolver a pasta da foto");

        Directory.CreateDirectory(directoryPath);
        await File.WriteAllBytesAsync(physicalPath, parsedPhoto.Bytes, cancellationToken);

        await DeleteAsync(currentFotoPerfil, cancellationToken);

        return LocalStoragePathHelper.BuildPublicUrl(_localOptions, StorageFolder, relativePath);
    }

    public Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fotoPerfil) || IsDataUrl(fotoPerfil))
        {
            return Task.CompletedTask;
        }

        var relativePath = LocalStoragePathHelper.TryGetRelativePath(_localOptions, StorageFolder, fotoPerfil);
        if (relativePath == null)
        {
            return Task.CompletedTask;
        }

        var physicalPath = LocalStoragePathHelper.GetPhysicalPath(_localOptions, StorageFolder, relativePath);

        try
        {
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel remover a foto de perfil do disco local");
        }

        return Task.CompletedTask;
    }

    public async Task<ProfilePhotoFile?> GetAsync(string? fotoPerfil, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fotoPerfil))
        {
            return null;
        }

        if (IsDataUrl(fotoPerfil))
        {
            var parsedPhoto = ParseDataUrl(fotoPerfil);
            return new ProfilePhotoFile(new MemoryStream(parsedPhoto.Bytes), parsedPhoto.ContentType);
        }

        var relativePath = LocalStoragePathHelper.TryGetRelativePath(_localOptions, StorageFolder, fotoPerfil);
        if (relativePath == null)
        {
            return null;
        }

        var physicalPath = LocalStoragePathHelper.GetPhysicalPath(_localOptions, StorageFolder, relativePath);
        if (!File.Exists(physicalPath))
        {
            return null;
        }

        var extension = Path.GetExtension(physicalPath);
        var contentType = ContentTypesByExtension.TryGetValue(extension, out var resolvedContentType)
            ? resolvedContentType
            : "application/octet-stream";
        var stream = File.OpenRead(physicalPath);

        return await Task.FromResult(new ProfilePhotoFile(stream, contentType));
    }

    private static bool IsDataUrl(string value)
    {
        return value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }

    private static ParsedProfilePhoto ParseDataUrl(string dataUrl)
    {
        var commaIndex = dataUrl.IndexOf(',');

        if (commaIndex <= 0)
        {
            throw new InvalidOperationException("Foto de perfil invalida");
        }

        var header = dataUrl[..commaIndex];

        if (!header.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || !header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Foto de perfil invalida");
        }

        var contentType = header[5..^7];

        if (!AllowedContentTypes.TryGetValue(contentType, out var extension))
        {
            throw new InvalidOperationException("Use uma foto PNG, JPG ou WEBP");
        }

        try
        {
            return new ParsedProfilePhoto(
                contentType,
                extension,
                Convert.FromBase64String(dataUrl[(commaIndex + 1)..]));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Foto de perfil invalida", ex);
        }
    }

    private sealed record ParsedProfilePhoto(string ContentType, string Extension, byte[] Bytes);
}
