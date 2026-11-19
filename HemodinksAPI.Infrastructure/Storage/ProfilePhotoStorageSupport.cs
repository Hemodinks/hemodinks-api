using HemodinksAPI.Domain.Exceptions;

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

            throw new BusinessRuleException(UnreadablePhotoMessage);
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

    internal const string UnreadablePhotoMessage =
        "Não foi possível ler a foto enviada. Selecione a imagem novamente (PNG, JPG ou WEBP).";

    internal const string UnsupportedFormatMessage =
        "Formato de imagem não aceito. Use uma foto PNG, JPG ou WEBP.";

    private static void ValidateSize(int bytesLength, long maxBytes)
    {
        if (bytesLength > maxBytes)
        {
            throw new BusinessRuleException(
                $"A foto selecionada tem {FormatMegabytes(bytesLength)} e o limite é {FormatMegabytes(maxBytes)}. "
                + "Reduza ou comprima a imagem e envie novamente.");
        }
    }

    // Arredonda para cima: 2 MB + 1 byte aparece como "2,1 MB", nunca como "2 MB" (igual ao limite).
    internal static string FormatMegabytes(long bytes)
    {
        var tenths = Math.Ceiling(bytes / (1024d * 1024d) * 10) / 10;
        return $"{tenths.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} MB";
    }

    private static ParsedProfilePhoto ParseDataUrl(string dataUrl)
    {
        var commaIndex = dataUrl.IndexOf(',');

        if (commaIndex <= 0)
        {
            throw new BusinessRuleException(UnreadablePhotoMessage);
        }

        var header = dataUrl[..commaIndex];

        if (!header.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || !header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(UnreadablePhotoMessage);
        }

        var contentType = header[5..^7];

        if (!AllowedContentTypes.TryGetValue(contentType, out var extension))
        {
            throw new BusinessRuleException(UnsupportedFormatMessage);
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
            throw new BusinessRuleException(UnreadablePhotoMessage, ex);
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
