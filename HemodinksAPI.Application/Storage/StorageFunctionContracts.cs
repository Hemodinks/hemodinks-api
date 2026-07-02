namespace HemodinksAPI.Application.Storage;

public sealed record RemoteProfilePhotoSaveRequest(
    string? FotoPerfil,
    string? CurrentFotoPerfil);

public sealed record RemoteProfilePhotoSaveResponse(
    string? FotoPerfil);

public sealed record RemotePatientFileUploadRequest(
    string FileName,
    string? ContentType,
    string Base64Content);

public sealed record RemotePatientFileUploadResponse(
    string OriginalName,
    string ContentType,
    long SizeBytes,
    string Url);
