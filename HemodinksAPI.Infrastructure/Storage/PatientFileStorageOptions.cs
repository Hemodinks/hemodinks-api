namespace HemodinksAPI.Infrastructure.Storage;

public class PatientFileStorageOptions
{
    public string? ConnectionString { get; set; }

    public string ContainerName { get; set; } = "patient-files";

    public string? PublicBaseUrl { get; set; }

    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
}
