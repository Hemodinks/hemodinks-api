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

        var blobName = GetBlobNameFromUrl(fotoPerfil);

        if (string.IsNullOrWhiteSpace(blobName))
        {
            return;
        }

        try
        {
            var containerClient = await GetContainerClientAsync(cancellationToken);
            await containerClient.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
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

        var blobName = GetBlobNameFromUrl(fotoPerfil);

        if (string.IsNullOrWhiteSpace(blobName))
        {
            return null;
        }

        var containerClient = await GetContainerClientAsync(cancellationToken);
        var blobClient = containerClient.GetBlobClient(blobName);

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

    private async Task<BlobContainerClient> GetContainerClientAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("AzureStorage:ConnectionString deve ser configurado para salvar fotos de perfil");
        }

        if (string.IsNullOrWhiteSpace(_options.ContainerName))
        {
            throw new InvalidOperationException("AzureStorage:ContainerName deve ser configurado para salvar fotos de perfil");
        }

        var containerClient = new BlobContainerClient(_options.ConnectionString, _options.ContainerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
        return containerClient;
    }

    private string BuildPublicUrl(BlobClient blobClient, string blobName)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return blobClient.Uri.ToString();
        }

        return $"{_options.PublicBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(blobName).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)}";
    }

    private string? GetBlobNameFromUrl(string fotoPerfil)
    {
        if (!Uri.TryCreate(fotoPerfil, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            var publicBaseUrl = _options.PublicBaseUrl.TrimEnd('/');

            if (fotoPerfil.StartsWith($"{publicBaseUrl}/", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(fotoPerfil[(publicBaseUrl.Length + 1)..]);
            }
        }

        var containerPath = $"/{_options.ContainerName.Trim('/')}/";
        var index = uri.AbsolutePath.IndexOf(containerPath, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return null;
        }

        return Uri.UnescapeDataString(uri.AbsolutePath[(index + containerPath.Length)..]);
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
