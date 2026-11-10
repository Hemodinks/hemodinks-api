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

    public Task<StoredPatientFile> SaveAsync(UploadedFile file, int clinicaId, CancellationToken cancellationToken)
    {
        PatientFileBlobPath.EnsureValidClinicaId(clinicaId);
        return SaveForStorageFunctionAsync(file, clinicaId, cancellationToken);
    }

    // Só a Function usa null: upload vindo de uma API anterior ao #226, sem X-Clinica-Id (compatibilidade de rollout).
    // Fora da Function, use SaveAsync, que exige clínica válida.
    public async Task<StoredPatientFile> SaveForStorageFunctionAsync(UploadedFile file, int? clinicaId, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var validated = await PatientFileValidation.ValidateAsync(file, stream, _options.MaxBytes, cancellationToken);

        var containerClient = await GetContainerClientAsync(cancellationToken);
        var blobName = clinicaId.HasValue
            ? PatientFileBlobPath.Build(clinicaId.Value, validated.Extension, DateTime.UtcNow)
            : PatientFileBlobPath.BuildLegacy(validated.Extension, DateTime.UtcNow);
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

    public async Task DeleteAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken)
    {
        var location = GetBlobLocationFromUrl(fileUrl);

        if (location == null)
        {
            return;
        }

        // Dono verificado no blob que será de fato excluído (não na URL original: evita bypass com ? ou #).
        if (!PatientFileBlobPath.BelongsTo(location.BlobName, clinicaId))
        {
            _logger.LogWarning("Exclusao de arquivo recusada: prefixo de clinica nao corresponde ao registro.");
            return;
        }

        try
        {
            var containerClient = await GetContainerClientAsync(
                cancellationToken,
                createIfMissing: false,
                location.ContainerName);
            await containerClient.GetBlobClient(location.BlobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel remover o arquivo do paciente do Azure Storage");
        }
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(
        CancellationToken cancellationToken,
        bool createIfMissing = true,
        string? containerName = null,
        BlobClientOptions? clientOptions = null)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("AzureStorage:ConnectionString deve ser configurado para salvar arquivos de paciente");
        }

        var resolvedContainerName = string.IsNullOrWhiteSpace(containerName)
            ? _options.ContainerName
            : containerName;

        if (string.IsNullOrWhiteSpace(resolvedContainerName))
        {
            throw new InvalidOperationException("AzureStorage:PatientFilesContainerName deve ser configurado para salvar arquivos de paciente");
        }

        var containerClient = new BlobContainerClient(_options.ConnectionString, resolvedContainerName, clientOptions);
        if (createIfMissing)
        {
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        return containerClient;
    }

    public async Task<StoredPatientFileContent?> GetAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken)
    {
        var location = GetBlobLocationFromUrl(fileUrl);

        if (location == null)
        {
            return null;
        }

        // Dono verificado no blob que será de fato lido (não na URL original: evita bypass com ? ou #).
        // 404 genérico: não revela que o arquivo existe em outra clínica.
        if (!PatientFileBlobPath.BelongsTo(location.BlobName, clinicaId))
        {
            _logger.LogWarning("Leitura de arquivo recusada: prefixo de clinica nao corresponde ao registro.");
            throw new KeyNotFoundException("Arquivo nao encontrado.");
        }

        var readTimeout = BlobReadResilience.ResolveTimeout(_options.ReadTimeoutSeconds);
        using var readBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readBudget.CancelAfter(readTimeout * 2 + TimeSpan.FromSeconds(2));

        try
        {
            var containerClient = await GetContainerClientAsync(
                readBudget.Token,
                createIfMissing: false,
                location.ContainerName,
                BlobReadResilience.CreateReadClientOptions(readTimeout));
            var blobClient = containerClient.GetBlobClient(location.BlobName);

            if (!(await blobClient.ExistsAsync(readBudget.Token)).Value)
            {
                return null;
            }

            var response = await blobClient.DownloadStreamingAsync(cancellationToken: readBudget.Token);
            return new StoredPatientFileContent(response.Value.Content);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
        catch (Exception ex) when (BlobReadResilience.IsReadFailure(ex, cancellationToken))
        {
            // Mantém o comportamento de erro para arquivos clínicos (não fingir que não existem),
            // mas falha em segundos em vez de minutos.
            _logger.LogWarning(
                ex,
                "Nao foi possivel ler o arquivo no Azure Storage ({Failure}). Container: {ContainerName}. Blob: {BlobName}",
                BlobReadResilience.Describe(ex),
                location.ContainerName,
                location.BlobName);
            throw new TimeoutException("O armazenamento de arquivos nao respondeu a tempo.", ex);
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

    private AzureBlobPatientFileLocation? GetBlobLocationFromUrl(string? fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            return null;
        }

        return AzureBlobPatientFileLocationResolver.Resolve(
            fileUrl,
            _options.ContainerName,
            _options.PublicBaseUrl);
    }
}
