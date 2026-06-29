using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Storage;

public class LocalDiskProfilePhotoStorage : IProfilePhotoStorage
{
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

        var photoUrl = await SaveNewPhotoAsync(change.Photo!, cancellationToken);

        await DeleteAsync(currentFotoPerfil, cancellationToken);

        return photoUrl;
    }

    public Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fotoPerfil) || ProfilePhotoStorageSupport.IsDataUrl(fotoPerfil))
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

        if (ProfilePhotoStorageSupport.IsDataUrl(fotoPerfil))
        {
            return ProfilePhotoStorageSupport.ReadInlinePhoto(fotoPerfil);
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

    private async Task<string> SaveNewPhotoAsync(ParsedProfilePhoto photo, CancellationToken cancellationToken)
    {
        var relativePath = BuildRelativePath(photo.Extension);
        var physicalPath = LocalStoragePathHelper.GetPhysicalPath(_localOptions, StorageFolder, relativePath);
        var directoryPath = Path.GetDirectoryName(physicalPath)
            ?? throw new InvalidOperationException("Nao foi possivel resolver a pasta da foto");

        Directory.CreateDirectory(directoryPath);
        await File.WriteAllBytesAsync(physicalPath, photo.Bytes, cancellationToken);

        return LocalStoragePathHelper.BuildPublicUrl(_localOptions, StorageFolder, relativePath);
    }

    private static string BuildRelativePath(string extension)
    {
        return $"users/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension}";
    }
}
