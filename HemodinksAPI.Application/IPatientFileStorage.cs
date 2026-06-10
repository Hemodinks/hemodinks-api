namespace HemodinksAPI.Application.Storage;

public interface IPatientFileStorage
{
    Task<StoredPatientFile> SaveAsync(IFormFile file, CancellationToken cancellationToken);

    Task DeleteAsync(string? fileUrl, CancellationToken cancellationToken);
}

public sealed record StoredPatientFile(
    string OriginalName,
    string ContentType,
    long SizeBytes,
    string Url);
