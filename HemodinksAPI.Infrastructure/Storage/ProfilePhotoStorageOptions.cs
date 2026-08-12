namespace HemodinksAPI.Infrastructure.Storage;

public class ProfilePhotoStorageOptions
{
    public const long DefaultMaxBytes = 2 * 1024 * 1024;

    public string? ConnectionString { get; set; }

    public string ContainerName { get; set; } = "profile-photos";

    public string? PublicBaseUrl { get; set; }

    public long MaxBytes { get; set; } = DefaultMaxBytes;
}
