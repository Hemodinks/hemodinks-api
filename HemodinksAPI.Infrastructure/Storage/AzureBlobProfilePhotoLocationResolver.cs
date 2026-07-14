namespace HemodinksAPI.Infrastructure.Storage;

internal static class AzureBlobProfilePhotoLocationResolver
{
    public static AzureBlobProfilePhotoLocation? Resolve(
        string fotoPerfil,
        string defaultContainerName,
        string? publicBaseUrl)
    {
        var normalizedDefaultContainerName = defaultContainerName.Trim('/');

        if (!Uri.TryCreate(fotoPerfil, UriKind.Absolute, out var uri))
        {
            return GetBlobLocationFromPath(fotoPerfil, normalizedDefaultContainerName);
        }

        if (!string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            var normalizedPublicBaseUrl = publicBaseUrl.TrimEnd('/');

            if (fotoPerfil.StartsWith($"{normalizedPublicBaseUrl}/", StringComparison.OrdinalIgnoreCase))
            {
                return GetBlobLocationFromPath(
                    fotoPerfil[(normalizedPublicBaseUrl.Length + 1)..],
                    normalizedDefaultContainerName);
            }
        }

        return GetBlobLocationFromPath(uri.AbsolutePath.Trim('/'), normalizedDefaultContainerName);
    }

    private static AzureBlobProfilePhotoLocation? GetBlobLocationFromPath(
        string path,
        string defaultContainerName)
    {
        var normalizedPath = Uri.UnescapeDataString(path).Trim('/');

        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(defaultContainerName))
        {
            return null;
        }

        var directLocation = ResolveContainerRelativePath(normalizedPath, defaultContainerName);
        if (directLocation != null)
        {
            return directLocation;
        }

        var firstSlashIndex = normalizedPath.IndexOf('/');

        if (firstSlashIndex > 0)
        {
            var remainingPath = normalizedPath[(firstSlashIndex + 1)..];
            return ResolveContainerRelativePath(remainingPath, defaultContainerName);
        }

        return null;
    }

    private static AzureBlobProfilePhotoLocation? ResolveContainerRelativePath(
        string normalizedPath,
        string defaultContainerName)
    {
        var defaultContainerPrefix = $"{defaultContainerName}/";

        if (normalizedPath.StartsWith(defaultContainerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new AzureBlobProfilePhotoLocation(defaultContainerName, normalizedPath[defaultContainerPrefix.Length..]);
        }

        var firstSlashIndex = normalizedPath.IndexOf('/');

        if (firstSlashIndex > 0)
        {
            var firstSegment = normalizedPath[..firstSlashIndex];
            var remainingPath = normalizedPath[(firstSlashIndex + 1)..];

            if (firstSegment.StartsWith("profile-photos", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(remainingPath))
            {
                return new AzureBlobProfilePhotoLocation(firstSegment, remainingPath);
            }
        }

        return normalizedPath.StartsWith("users/", StringComparison.OrdinalIgnoreCase)
            ? new AzureBlobProfilePhotoLocation(defaultContainerName, normalizedPath)
            : null;
    }
}

internal sealed record AzureBlobProfilePhotoLocation(string ContainerName, string BlobName);
