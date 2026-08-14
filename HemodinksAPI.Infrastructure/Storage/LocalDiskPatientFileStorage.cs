using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Storage;

public class LocalDiskPatientFileStorage : IPatientFileStorage
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
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };

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

    public async Task<StoredPatientFile> SaveAsync(UploadedFile file, CancellationToken cancellationToken)
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
            throw new InvalidOperationException("Use arquivo PDF, DOC, DOCX, JPG, JPEG, PNG, XLS, XLSX, TXT, CSV, PPT ou PPTX");
        }

        var relativePath = $"pacientes/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var physicalPath = LocalStoragePathHelper.GetPhysicalPath(_localOptions, StorageFolder, relativePath);
        var directoryPath = Path.GetDirectoryName(physicalPath)
            ?? throw new InvalidOperationException("Nao foi possivel resolver a pasta do arquivo");

        Directory.CreateDirectory(directoryPath);

        await using var stream = File.Create(physicalPath);
        await file.CopyToAsync(stream, cancellationToken);

        return new StoredPatientFile(
            file.FileName,
            contentType,
            file.Length,
            LocalStoragePathHelper.BuildPublicUrl(_localOptions, StorageFolder, relativePath));
    }

    public Task DeleteAsync(string? fileUrl, CancellationToken cancellationToken)
    {
        var relativePath = LocalStoragePathHelper.TryGetRelativePath(_localOptions, StorageFolder, fileUrl);
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
            _logger.LogWarning(ex, "Nao foi possivel remover o arquivo do paciente do disco local");
        }

        return Task.CompletedTask;
    }

    public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, CancellationToken cancellationToken)
    {
        var relativePath = LocalStoragePathHelper.TryGetRelativePath(_localOptions, StorageFolder, fileUrl);
        if (relativePath == null)
        {
            return Task.FromResult<StoredPatientFileContent?>(null);
        }

        var physicalPath = LocalStoragePathHelper.GetPhysicalPath(_localOptions, StorageFolder, relativePath);
        return Task.FromResult<StoredPatientFileContent?>(File.Exists(physicalPath)
            ? new StoredPatientFileContent(File.OpenRead(physicalPath))
            : null);
    }
}
