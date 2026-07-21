namespace HemodinksAPI.Application.Storage;

public interface IPatientFileStorage
{
    Task<StoredPatientFile> SaveAsync(IFormFile file, CancellationToken cancellationToken);

    Task<StoredPatientFileContent?> GetAsync(string? fileUrl, CancellationToken cancellationToken);

    Task DeleteAsync(string? fileUrl, CancellationToken cancellationToken);
}

public sealed record StoredPatientFile(
    string OriginalName,
    string ContentType,
    long SizeBytes,
    string Url);

public sealed record StoredPatientFileContent(Stream Content) : IDisposable
{
    public void Dispose()
    {
        Content.Dispose();
    }
}

public sealed class PrivateFileDownload : IDisposable
{
    public Stream Content { get; init; } = Stream.Null;

    public string ContentType { get; init; } = "application/octet-stream";

    public string FileName { get; init; } = "arquivo";

    public void Dispose()
    {
        Content.Dispose();
    }
}
