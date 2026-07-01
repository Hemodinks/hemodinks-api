using HemodinksAPI.Application.Storage;

namespace HemodinksAPI.Infrastructure.Storage;

public class FunctionBackedPatientFileStorage : IPatientFileStorage
{
    private readonly StorageFunctionClient _storageFunctionClient;
    private readonly AzureBlobPatientFileStorage _fallbackStorage;

    public FunctionBackedPatientFileStorage(
        StorageFunctionClient storageFunctionClient,
        AzureBlobPatientFileStorage fallbackStorage)
    {
        _storageFunctionClient = storageFunctionClient;
        _fallbackStorage = fallbackStorage;
    }

    public async Task<StoredPatientFile> SaveAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        var response = await _storageFunctionClient.PostJsonAsync<RemotePatientFileUploadRequest, RemotePatientFileUploadResponse>(
            "storage/patient-file",
            new RemotePatientFileUploadRequest(
                file.FileName,
                file.ContentType,
                Convert.ToBase64String(memoryStream.ToArray())),
            cancellationToken);

        return new StoredPatientFile(
            response.OriginalName,
            response.ContentType,
            response.SizeBytes,
            response.Url);
    }

    public Task DeleteAsync(string? fileUrl, CancellationToken cancellationToken)
    {
        return _fallbackStorage.DeleteAsync(fileUrl, cancellationToken);
    }
}
