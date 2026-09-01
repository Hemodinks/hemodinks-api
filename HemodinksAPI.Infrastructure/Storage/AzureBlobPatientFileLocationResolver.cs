namespace HemodinksAPI.Infrastructure.Storage;

internal static class AzureBlobPatientFileLocationResolver
{
    public static AzureBlobPatientFileLocation? Resolve(
        string fileUrl,
        string defaultContainerName,
        string? publicBaseUrl)
    {
        var normalizedDefaultContainerName = defaultContainerName.Trim('/');

        if (string.IsNullOrWhiteSpace(normalizedDefaultContainerName))
        {
            return null;
        }

        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
        {
            return ResolvePath(fileUrl, normalizedDefaultContainerName);
        }

        if (!IsTrustedStorageHost(uri, publicBaseUrl))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            var normalizedPublicBaseUrl = publicBaseUrl.TrimEnd('/');

            if (fileUrl.StartsWith($"{normalizedPublicBaseUrl}/", StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = fileUrl[(normalizedPublicBaseUrl.Length + 1)..];
                var location = ResolvePath(relativePath, normalizedDefaultContainerName);

                if (location != null)
                {
                    return location;
                }
            }
        }

        return ResolvePath(uri.AbsolutePath, normalizedDefaultContainerName);
    }

    private static bool IsTrustedStorageHost(Uri uri, string? publicBaseUrl)
    {
        if (Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicBaseUri)
            && uri.Host.Equals(publicBaseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("azurite", StringComparison.OrdinalIgnoreCase);
    }

    private static AzureBlobPatientFileLocation? ResolvePath(
        string path,
        string defaultContainerName)
    {
        var normalizedPath = Uri.UnescapeDataString(path).Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var patientPathIndex = -1;

        for (var index = 0; index < segments.Length; index++)
        {
            if (segments[index].Equals("pacientes", StringComparison.OrdinalIgnoreCase))
            {
                patientPathIndex = index;
            }
        }

        if (patientPathIndex < 0 || patientPathIndex == segments.Length - 1)
        {
            return null;
        }

        return new AzureBlobPatientFileLocation(
            defaultContainerName,
            string.Join('/', segments[patientPathIndex..]));
    }
}

internal sealed record AzureBlobPatientFileLocation(string ContainerName, string BlobName);
