namespace HemodinksAPI.Infrastructure.Storage;

public class FunctionBackedPatientFileStorage : IPatientFileStorage
{
    private readonly StorageFunctionClient _storageFunctionClient;
    private readonly AzureBlobPatientFileStorage _fallbackStorage;
    private readonly PatientFileStorageOptions _options;

    public FunctionBackedPatientFileStorage(
        StorageFunctionClient storageFunctionClient,
        AzureBlobPatientFileStorage fallbackStorage,
        Microsoft.Extensions.Options.IOptions<PatientFileStorageOptions> options)
    {
        _storageFunctionClient = storageFunctionClient;
        _fallbackStorage = fallbackStorage;
        _options = options.Value;
    }

    public async Task<StoredPatientFile> SaveAsync(UploadedFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var validated = await PatientFileValidation.ValidateAsync(file, stream, _options.MaxBytes, cancellationToken);

        var response = await _storageFunctionClient.PostFileAsync<RemotePatientFileUploadResponse>(
            "storage/patient-file",
            validated.OriginalName,
            validated.ContentType,
            validated.SizeBytes,
            stream,
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

    public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, CancellationToken cancellationToken)
    {
        return _fallbackStorage.GetAsync(fileUrl, cancellationToken);
    }
}
