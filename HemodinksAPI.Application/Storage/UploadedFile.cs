namespace HemodinksAPI.Application.Storage;

public sealed class UploadedFile(
    string fileName,
    string contentType,
    long length,
    Func<Stream> openReadStream)
{
    public string FileName { get; } = fileName;
    public string ContentType { get; } = contentType;
    public long Length { get; } = length;

    public Stream OpenReadStream() => openReadStream();

    public async Task CopyToAsync(Stream target, CancellationToken cancellationToken)
    {
        await using var source = OpenReadStream();
        await source.CopyToAsync(target, cancellationToken);
    }
}
