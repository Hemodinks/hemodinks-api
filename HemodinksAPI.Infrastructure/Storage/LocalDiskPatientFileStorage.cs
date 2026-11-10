using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Storage;

public class LocalDiskPatientFileStorage : IPatientFileStorage
{
    private const string StorageFolder = "patient-files";

    private readonly PatientFileStorageOptions _options;
    private readonly LocalStorageOptions _localOptions;
    private readonly ILogger<LocalDiskPatientFileStorage> _logger;

    public LocalDiskPatientFileStorage(
        IOptions<PatientFileStorageOptions> options,
        IOptions<LocalStorageOptions> localOptions,
        ILogger<LocalDiskPatientFileStorage> logger)
    {
        _options = options.Value;
        _localOptions = localOptions.Value;
        _logger = logger;
    }

    public async Task<StoredPatientFile> SaveAsync(UploadedFile file, int clinicaId, CancellationToken cancellationToken)
    {
        PatientFileBlobPath.EnsureValidClinicaId(clinicaId);
        await using var source = file.OpenReadStream();
        var validated = await PatientFileValidation.ValidateAsync(file, source, _options.MaxBytes, cancellationToken);

        var relativePath = PatientFileBlobPath.Build(clinicaId, validated.Extension, DateTime.UtcNow);
        var physicalPath = LocalStoragePathHelper.GetPhysicalPath(_localOptions, StorageFolder, relativePath);
        var directoryPath = Path.GetDirectoryName(physicalPath)
            ?? throw new InvalidOperationException("Nao foi possivel resolver a pasta do arquivo");

        Directory.CreateDirectory(directoryPath);

        await using var stream = File.Create(physicalPath);
        await source.CopyToAsync(stream, cancellationToken);

        return new StoredPatientFile(
            validated.OriginalName,
            validated.ContentType,
            validated.SizeBytes,
            LocalStoragePathHelper.BuildPublicUrl(_localOptions, StorageFolder, relativePath));
    }

    public Task DeleteAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken)
    {
        var relativePath = LocalStoragePathHelper.TryGetRelativePath(_localOptions, StorageFolder, fileUrl);
        if (relativePath == null || !PatientFileBlobPath.BelongsTo(relativePath, clinicaId))
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
            _logger.LogWarning(ex, "Nao foi possivel remover o arquivo do paciente do disco local");
        }

        return Task.CompletedTask;
    }

    public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken)
    {
        var relativePath = LocalStoragePathHelper.TryGetRelativePath(_localOptions, StorageFolder, fileUrl);
        if (relativePath == null)
        {
            return Task.FromResult<StoredPatientFileContent?>(null);
        }

        if (!PatientFileBlobPath.BelongsTo(relativePath, clinicaId))
        {
            // 404 genérico: não revela que o arquivo existe em outra clínica.
            _logger.LogWarning("Leitura de arquivo recusada: prefixo de clinica nao corresponde ao registro.");
            throw new KeyNotFoundException("Arquivo nao encontrado.");
        }

        var physicalPath = LocalStoragePathHelper.GetPhysicalPath(_localOptions, StorageFolder, relativePath);
        return Task.FromResult<StoredPatientFileContent?>(File.Exists(physicalPath)
            ? new StoredPatientFileContent(File.OpenRead(physicalPath))
            : null);
    }
}
