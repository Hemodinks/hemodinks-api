using System.Net;
using System.Text.Json;
using System.Text;
using HemodinksAPI.Application.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HemodinksAPI.Infrastructure.Storage;

namespace HemodinksAPI.Workers.Functions;

public class StorageUploadFunctions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<StorageUploadFunctions> _logger;
    private readonly PatientFileStorageOptions _patientFileOptions;

    public StorageUploadFunctions(
        IProfilePhotoStorage profilePhotoStorage,
        IPatientFileStorage patientFileStorage,
        IOptions<PatientFileStorageOptions> patientFileOptions,
        ILogger<StorageUploadFunctions> logger)
    {
        _profilePhotoStorage = profilePhotoStorage;
        _patientFileStorage = patientFileStorage;
        _patientFileOptions = patientFileOptions.Value;
        _logger = logger;
    }

    [Function(nameof(UploadProfilePhoto))]
    public async Task<HttpResponseData> UploadProfilePhoto(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "storage/profile-photo")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var payload = await JsonSerializer.DeserializeAsync<RemoteProfilePhotoSaveRequest>(
            request.Body,
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("Payload de upload de foto de perfil invalido.");

        var fotoPerfil = await _profilePhotoStorage.SaveAsync(
            payload.FotoPerfil,
            payload.CurrentFotoPerfil,
            cancellationToken);

        _logger.LogInformation("Upload de foto de perfil processado pela Function.");

        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(new RemoteProfilePhotoSaveResponse(fotoPerfil), JsonOptions),
            cancellationToken);
        return response;
    }

    [Function(nameof(UploadPatientFile))]
    public async Task<HttpResponseData> UploadPatientFile(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "storage/patient-file")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var encodedFileName = request.Headers.TryGetValues("X-File-Name-Base64", out var fileNameValues)
            ? fileNameValues.SingleOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(encodedFileName))
        {
            throw new InvalidOperationException("Nome do arquivo obrigatorio.");
        }

        string fileName;
        try
        {
            fileName = Encoding.UTF8.GetString(Convert.FromBase64String(encodedFileName));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Nome do arquivo invalido.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"hemodinks-upload-{Guid.NewGuid():N}.tmp");
        await using var tempFile = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        var fileLength = await CopyWithLimitAsync(
            request.Body,
            tempFile,
            _patientFileOptions.MaxBytes,
            cancellationToken);
        tempFile.Position = 0;

        var uploadedFile = new UploadedFile(
            fileName,
            request.Headers.TryGetValues("Content-Type", out var contentTypes)
                ? contentTypes.SingleOrDefault() ?? "application/octet-stream"
                : "application/octet-stream",
            fileLength,
            () => tempFile);

        var storedFile = await _patientFileStorage.SaveAsync(uploadedFile, cancellationToken);

        _logger.LogInformation("Upload de arquivo processado pela Function");

        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(new RemotePatientFileUploadResponse(
                storedFile.OriginalName,
                storedFile.ContentType,
                storedFile.SizeBytes,
                storedFile.Url), JsonOptions),
            cancellationToken);
        return response;
    }

    private static async Task<long> CopyWithLimitAsync(
        Stream source,
        Stream target,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return total;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException($"O arquivo deve ter no máximo {maxBytes / 1024 / 1024} MB");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
