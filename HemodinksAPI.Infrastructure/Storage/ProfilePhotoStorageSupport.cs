namespace HemodinksAPI.Infrastructure.Storage;

internal static class ProfilePhotoStorageSupport
{
    private static readonly IReadOnlyDictionary<string, string> AllowedContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

    public static RequestedProfilePhotoChange EvaluateRequestedChange(
        string? requestedPhoto,
        string? currentPhoto,
        long maxBytes)
    {
        if (string.IsNullOrWhiteSpace(requestedPhoto))
        {
            return RequestedProfilePhotoChange.RemoveCurrent();
        }

        if (!IsDataUrl(requestedPhoto))
        {
            if (!string.IsNullOrWhiteSpace(currentPhoto)
                && string.Equals(requestedPhoto, currentPhoto, StringComparison.Ordinal))
            {
                return RequestedProfilePhotoChange.KeepCurrent();
            }

            throw new InvalidOperationException("Foto de perfil invalida");
        }

        var parsedPhoto = ParseDataUrl(requestedPhoto);
        ValidateSize(parsedPhoto.Bytes.Length, maxBytes);

        return RequestedProfilePhotoChange.UploadNew(parsedPhoto);
    }

    public static ProfilePhotoFile ReadInlinePhoto(string dataUrl)
    {
        var parsedPhoto = ParseDataUrl(dataUrl);
        return new ProfilePhotoFile(new MemoryStream(parsedPhoto.Bytes), parsedPhoto.ContentType);
    }

    public static bool IsDataUrl(string value)
    {
        return value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateSize(int bytesLength, long maxBytes)
    {
        if (bytesLength > maxBytes)
        {
            throw new InvalidOperationException($"A foto deve ter no maximo {maxBytes / 1024 / 1024} MB");
        }
    }

    private static ParsedProfilePhoto ParseDataUrl(string dataUrl)
    {
        var commaIndex = dataUrl.IndexOf(',');

        if (commaIndex <= 0)
        {
            throw new InvalidOperationException("Foto de perfil invalida");
        }

        var header = dataUrl[..commaIndex];

        if (!header.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || !header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Foto de perfil invalida");
        }

        var contentType = header[5..^7];

        if (!AllowedContentTypes.TryGetValue(contentType, out var extension))
        {
            throw new InvalidOperationException("Use uma foto PNG, JPG ou WEBP");
        }

        try
        {
            return new ParsedProfilePhoto(
                contentType,
                extension,
                Convert.FromBase64String(dataUrl[(commaIndex + 1)..]));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Foto de perfil invalida", ex);
        }
    }
}

internal sealed record ParsedProfilePhoto(string ContentType, string Extension, byte[] Bytes);

internal sealed record RequestedProfilePhotoChange(
    ProfilePhotoChangeKind Kind,
    ParsedProfilePhoto? Photo = null)
{
    public static RequestedProfilePhotoChange RemoveCurrent()
    {
        return new RequestedProfilePhotoChange(ProfilePhotoChangeKind.RemoveCurrent);
    }

    public static RequestedProfilePhotoChange KeepCurrent()
    {
        return new RequestedProfilePhotoChange(ProfilePhotoChangeKind.KeepCurrent);
    }

    public static RequestedProfilePhotoChange UploadNew(ParsedProfilePhoto photo)
    {
        return new RequestedProfilePhotoChange(ProfilePhotoChangeKind.UploadNew, photo);
    }
}

internal enum ProfilePhotoChangeKind
{
    RemoveCurrent,
    KeepCurrent,
    UploadNew
}
