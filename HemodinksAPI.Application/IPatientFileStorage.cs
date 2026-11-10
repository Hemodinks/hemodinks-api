namespace HemodinksAPI.Application.Storage;

public interface IPatientFileStorage
{
    // clinicaId vem sempre do registro carregado sob o filtro de tenant (nunca do payload).
    // Novos arquivos ficam em clinicas/{clinicaId}/pacientes/... (PatientFileBlobPath).
    Task<StoredPatientFile> SaveAsync(UploadedFile file, int clinicaId, CancellationToken cancellationToken);

    // Arquivo com prefixo de outra clínica é tratado como inexistente (defesa em profundidade).
    Task<StoredPatientFileContent?> GetAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken);

    Task DeleteAsync(string? fileUrl, int clinicaId, CancellationToken cancellationToken);
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

public sealed class StoredFileUnavailableException(string message) : IOException(message);

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
