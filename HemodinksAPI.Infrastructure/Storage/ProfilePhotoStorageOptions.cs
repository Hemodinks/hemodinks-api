namespace HemodinksAPI.Infrastructure.Storage;

public class ProfilePhotoStorageOptions
{
    public string? ConnectionString { get; set; }

    public string ContainerName { get; set; } = "profile-photos";

    public string? PublicBaseUrl { get; set; }

    public long MaxBytes { get; set; } = 1024 * 1024;
}
