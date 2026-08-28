using System.Net;
using System.Text.Json;
using HemodinksAPI.Application.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Workers.Functions;

public class StorageUploadFunctions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<StorageUploadFunctions> _logger;

    public StorageUploadFunctions(
        IProfilePhotoStorage profilePhotoStorage,
        IPatientFileStorage patientFileStorage,
        ILogger<StorageUploadFunctions> logger)
    {
        _profilePhotoStorage = profilePhotoStorage;
        _patientFileStorage = patientFileStorage;
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
        var payload = await JsonSerializer.DeserializeAsync<RemotePatientFileUploadRequest>(
            request.Body,
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("Payload de upload de arquivo invalido.");

        if (string.IsNullOrWhiteSpace(payload.FileName))
        {
            throw new InvalidOperationException("Nome do arquivo obrigatorio.");
        }

        var fileBytes = Convert.FromBase64String(payload.Base64Content);
        var uploadedFile = new UploadedFile(
            payload.FileName,
            payload.ContentType ?? "application/octet-stream",
            fileBytes.LongLength,
            () => new MemoryStream(fileBytes, writable: false));

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
}
