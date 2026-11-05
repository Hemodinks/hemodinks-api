namespace HemodinksAPI.Infrastructure.Storage;

public class PatientFileStorageOptions
{
    public string? ConnectionString { get; set; }

    public string ContainerName { get; set; } = "patient-files";

    public string? PublicBaseUrl { get; set; }

    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Tempo máximo, em segundos, de cada tentativa de leitura no Blob Storage.</summary>
    public int ReadTimeoutSeconds { get; set; } = BlobReadResilience.DefaultReadTimeoutSeconds;
}
