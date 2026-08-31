using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Storage;

public class AzureBlobPatientFileStorage : IPatientFileStorage
{
    private readonly PatientFileStorageOptions _options;
    private readonly ILogger<AzureBlobPatientFileStorage> _logger;

    public AzureBlobPatientFileStorage(
        IOptions<PatientFileStorageOptions> options,
        ILogger<AzureBlobPatientFileStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoredPatientFile> SaveAsync(UploadedFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var validated = await PatientFileValidation.ValidateAsync(file, stream, _options.MaxBytes, cancellationToken);

        var containerClient = await GetContainerClientAsync(cancellationToken);
        var blobName = $"pacientes/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{validated.Extension}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = validated.ContentType,
                CacheControl = "private, max-age=3600"
            }
        }, cancellationToken);

        return new StoredPatientFile(
            validated.OriginalName,
            validated.ContentType,
            validated.SizeBytes,
            BuildPublicUrl(blobClient, blobName));
    }

    public async Task DeleteAsync(string? fileUrl, CancellationToken cancellationToken)
    {
        var blobName = GetBlobNameFromUrl(fileUrl);

        if (string.IsNullOrWhiteSpace(blobName))
        {
            return;
        }

        try
        {
            var containerClient = await GetContainerClientAsync(cancellationToken, createIfMissing: false);
            await containerClient.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel remover o arquivo do paciente do Azure Storage");
        }
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(
        CancellationToken cancellationToken,
        bool createIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("AzureStorage:ConnectionString deve ser configurado para salvar arquivos de paciente");
        }

        if (string.IsNullOrWhiteSpace(_options.ContainerName))
        {
            throw new InvalidOperationException("AzureStorage:PatientFilesContainerName deve ser configurado para salvar arquivos de paciente");
        }

        var containerClient = new BlobContainerClient(_options.ConnectionString, _options.ContainerName);
        if (createIfMissing)
        {
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        return containerClient;
    }

    public async Task<StoredPatientFileContent?> GetAsync(string? fileUrl, CancellationToken cancellationToken)
    {
        var blobName = GetBlobNameFromUrl(fileUrl);

        if (string.IsNullOrWhiteSpace(blobName))
        {
            return null;
        }

        try
        {
            var containerClient = await GetContainerClientAsync(cancellationToken, createIfMissing: false);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!(await blobClient.ExistsAsync(cancellationToken)).Value)
            {
                return null;
            }

            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return new StoredPatientFileContent(response.Value.Content);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
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

    private string? GetBlobNameFromUrl(string? fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl) || !Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            var publicBaseUrl = _options.PublicBaseUrl.TrimEnd('/');

            if (fileUrl.StartsWith($"{publicBaseUrl}/", StringComparison.OrdinalIgnoreCase))
            {
                var blobName = Uri.UnescapeDataString(fileUrl[(publicBaseUrl.Length + 1)..]);
                var containerPrefix = $"{_options.ContainerName.Trim('/')}/";

                if (blobName.StartsWith(containerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return blobName[containerPrefix.Length..];
                }

                return blobName;
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
}
