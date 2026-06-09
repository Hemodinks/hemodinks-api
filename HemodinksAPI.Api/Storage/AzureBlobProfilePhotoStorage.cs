using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Api.Storage;

public class AzureBlobProfilePhotoStorage : IProfilePhotoStorage
{
    private static readonly IReadOnlyDictionary<string, string> AllowedContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

    private readonly ProfilePhotoStorageOptions _options;
    private readonly ILogger<AzureBlobProfilePhotoStorage> _logger;

    public AzureBlobProfilePhotoStorage(
        IOptions<ProfilePhotoStorageOptions> options,
        ILogger<AzureBlobProfilePhotoStorage> logger)
    {
        _options = options.Value;
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

        var containerClient = await GetContainerClientAsync(cancellationToken);
        var blobName = $"users/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{parsedPhoto.Extension}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await using var stream = new MemoryStream(parsedPhoto.Bytes);
        await blobClient.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = parsedPhoto.ContentType,
                CacheControl = "public, max-age=31536000"
            }
        }, cancellationToken);

        await DeleteAsync(currentFotoPerfil, cancellationToken);

        return BuildPublicUrl(blobClient, blobName);
    }

    public async Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fotoPerfil) || IsDataUrl(fotoPerfil))
        {
            return;
        }

        var location = GetBlobLocationFromUrl(fotoPerfil);

        if (location == null)
        {
            return;
        }

        try
        {
            var containerClient = await GetContainerClientAsync(cancellationToken, location.ContainerName);
            await containerClient.GetBlobClient(location.BlobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel remover a foto de perfil do Azure Storage");
        }
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

        var location = GetBlobLocationFromUrl(fotoPerfil);

        if (location == null)
        {
            return null;
        }

        var containerClient = await GetContainerClientAsync(cancellationToken, location.ContainerName);
        var blobClient = containerClient.GetBlobClient(location.BlobName);

        if (!(await blobClient.ExistsAsync(cancellationToken)).Value)
        {
            return null;
        }

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        var contentType = string.IsNullOrWhiteSpace(response.Value.Details.ContentType)
            ? "application/octet-stream"
            : response.Value.Details.ContentType;

        return new ProfilePhotoFile(response.Value.Content, contentType);
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(CancellationToken cancellationToken, string? containerName = null)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("AzureStorage:ConnectionString deve ser configurado para salvar fotos de perfil");
        }

        var resolvedContainerName = string.IsNullOrWhiteSpace(containerName)
            ? _options.ContainerName
            : containerName;

        if (string.IsNullOrWhiteSpace(resolvedContainerName))
        {
            throw new InvalidOperationException("AzureStorage:ContainerName deve ser configurado para salvar fotos de perfil");
        }

        var containerClient = new BlobContainerClient(_options.ConnectionString, resolvedContainerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
        return containerClient;
    }

    private string BuildPublicUrl(BlobClient blobClient, string blobName)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return blobClient.Uri.ToString();
        }

        var publicBaseUrl = _options.PublicBaseUrl.TrimEnd('/');
        var encodedBlobName = Uri.EscapeDataString(blobName).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);

        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri)
            || uri.AbsolutePath.Trim('/').EndsWith(_options.ContainerName.Trim('/'), StringComparison.OrdinalIgnoreCase))
        {
            return $"{publicBaseUrl}/{encodedBlobName}";
        }

        return $"{publicBaseUrl}/{_options.ContainerName.Trim('/')}/{encodedBlobName}";
    }

    private BlobLocation? GetBlobLocationFromUrl(string fotoPerfil)
    {
        if (!Uri.TryCreate(fotoPerfil, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var defaultContainerName = _options.ContainerName.Trim('/');

        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            var publicBaseUrl = _options.PublicBaseUrl.TrimEnd('/');

            if (fotoPerfil.StartsWith($"{publicBaseUrl}/", StringComparison.OrdinalIgnoreCase))
            {
                return GetBlobLocationFromPath(fotoPerfil[(publicBaseUrl.Length + 1)..], defaultContainerName);
            }
        }

        return GetBlobLocationFromPath(uri.AbsolutePath.Trim('/'), defaultContainerName);
    }

    private static BlobLocation? GetBlobLocationFromPath(string path, string defaultContainerName)
    {
        var normalizedPath = Uri.UnescapeDataString(path).Trim('/');

        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(defaultContainerName))
        {
            return null;
        }

        var defaultContainerPrefix = $"{defaultContainerName}/";

        if (normalizedPath.StartsWith(defaultContainerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new BlobLocation(defaultContainerName, normalizedPath[defaultContainerPrefix.Length..]);
        }

        var firstSlashIndex = normalizedPath.IndexOf('/');

        if (firstSlashIndex > 0)
        {
            var firstSegment = normalizedPath[..firstSlashIndex];
            var remainingPath = normalizedPath[(firstSlashIndex + 1)..];

            if (firstSegment.StartsWith("profile-photos", StringComparison.OrdinalIgnoreCase)
                && remainingPath.StartsWith("users/", StringComparison.OrdinalIgnoreCase))
            {
                return new BlobLocation(firstSegment, remainingPath);
            }
        }

        return normalizedPath.StartsWith("users/", StringComparison.OrdinalIgnoreCase)
            ? new BlobLocation(defaultContainerName, normalizedPath)
            : null;
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

    private sealed record BlobLocation(string ContainerName, string BlobName);
}
