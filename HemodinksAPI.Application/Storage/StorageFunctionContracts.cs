namespace HemodinksAPI.Application.Storage;

public sealed record RemoteProfilePhotoSaveRequest(
    string? FotoPerfil,
    string? CurrentFotoPerfil);

public sealed record RemoteProfilePhotoSaveResponse(
    string? FotoPerfil);

public sealed record RemotePatientFileUploadResponse(
    string OriginalName,
    string ContentType,
    long SizeBytes,
    string Url);
