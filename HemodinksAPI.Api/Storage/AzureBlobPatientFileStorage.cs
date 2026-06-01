using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Api.Storage;

public class AzureBlobPatientFileStorage : IPatientFileStorage
{
    private static readonly IReadOnlyDictionary<string, string> AllowedExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    private readonly PatientFileStorageOptions _options;
    private readonly ILogger<AzureBlobPatientFileStorage> _logger;

    public AzureBlobPatientFileStorage(
        IOptions<PatientFileStorageOptions> options,
        ILogger<AzureBlobPatientFileStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoredPatientFile> SaveAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Arquivo vazio");
        }

        if (file.Length > _options.MaxBytes)
        {
            throw new InvalidOperationException($"O arquivo deve ter no maximo {_options.MaxBytes / 1024 / 1024} MB");
        }

        var extension = Path.GetExtension(file.FileName);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.TryGetValue(extension, out var contentType))
        {
            throw new InvalidOperationException("Use arquivo PDF, DOC, DOCX, JPG, JPEG, PNG, XLS ou XLSX");
        }

        var containerClient = await GetContainerClientAsync(cancellationToken);
        var blobName = $"pacientes/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType,
                CacheControl = "private, max-age=3600"
            }
        }, cancellationToken);

        return new StoredPatientFile(
            file.FileName,
            contentType,
            file.Length,
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
            var containerClient = await GetContainerClientAsync(cancellationToken);
            await containerClient.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel remover o arquivo do paciente do Azure Storage");
        }
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(CancellationToken cancellationToken)
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
                return Uri.UnescapeDataString(fileUrl[(publicBaseUrl.Length + 1)..]);
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
