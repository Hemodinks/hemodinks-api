using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Storage;

public class AzureBlobProfilePhotoStorage : IProfilePhotoStorage
{
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
        var change = ProfilePhotoStorageSupport.EvaluateRequestedChange(
            fotoPerfil,
            currentFotoPerfil,
            _options.MaxBytes);

        if (change.Kind == ProfilePhotoChangeKind.RemoveCurrent)
        {
            await DeleteAsync(currentFotoPerfil, cancellationToken);
            return null;
        }

        if (change.Kind == ProfilePhotoChangeKind.KeepCurrent)
        {
            return currentFotoPerfil;
        }

        var uploadedPhotoUrl = await UploadPhotoAsync(change.Photo!, cancellationToken);

        await DeleteAsync(currentFotoPerfil, cancellationToken);

        return uploadedPhotoUrl;
    }

    public async Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fotoPerfil) || ProfilePhotoStorageSupport.IsDataUrl(fotoPerfil))
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
            var containerClient = await GetContainerClientAsync(
                cancellationToken,
                location.ContainerName,
                createIfMissing: false);
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

        if (ProfilePhotoStorageSupport.IsDataUrl(fotoPerfil))
        {
            return ProfilePhotoStorageSupport.ReadInlinePhoto(fotoPerfil);
        }

        var location = GetBlobLocationFromUrl(fotoPerfil);

        if (location == null)
        {
            return null;
        }

        try
        {
            var containerClient = await GetContainerClientAsync(
                cancellationToken,
                location.ContainerName,
                createIfMissing: false);
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
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(
                ex,
                "Nao foi possivel buscar a foto de perfil no Azure Storage. Container: {ContainerName}. Blob: {BlobName}",
                location.ContainerName,
                location.BlobName);
            return null;
        }
    }

    private async Task<string> UploadPhotoAsync(ParsedProfilePhoto photo, CancellationToken cancellationToken)
    {
        var containerClient = await GetContainerClientAsync(cancellationToken);
        var blobName = BuildBlobName(photo.Extension);
        var blobClient = containerClient.GetBlobClient(blobName);

        await using var stream = new MemoryStream(photo.Bytes);
        await blobClient.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = photo.ContentType,
                CacheControl = "public, max-age=31536000"
            }
        }, cancellationToken);

        return BuildPublicUrl(blobClient, blobName);
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(
        CancellationToken cancellationToken,
        string? containerName = null,
        bool createIfMissing = true)
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
        if (createIfMissing)
        {
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        return containerClient;
    }

    private static string BuildBlobName(string extension)
    {
        return $"users/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension}";
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

    private AzureBlobProfilePhotoLocation? GetBlobLocationFromUrl(string fotoPerfil)
    {
        return AzureBlobProfilePhotoLocationResolver.Resolve(
            fotoPerfil,
            _options.ContainerName,
            _options.PublicBaseUrl);
    }
}
