namespace HemodinksAPI.Infrastructure.Storage;

public static class LocalStoragePathHelper
{
    public static string ResolveRootPath(string? configuredRootPath, string contentRootPath)
    {
        var rootPath = string.IsNullOrWhiteSpace(configuredRootPath)
            ? LocalStorageOptions.DefaultRootPath
            : configuredRootPath.Trim();

        return Path.IsPathRooted(rootPath)
            ? rootPath
            : Path.GetFullPath(Path.Combine(contentRootPath, rootPath));
    }

    public static string NormalizeRequestPath(string? requestPath)
    {
        var normalizedRequestPath = string.IsNullOrWhiteSpace(requestPath)
            ? LocalStorageOptions.DefaultRequestPath
            : requestPath.Trim();

        if (!normalizedRequestPath.StartsWith('/'))
        {
            normalizedRequestPath = $"/{normalizedRequestPath}";
        }

        return normalizedRequestPath.TrimEnd('/');
    }

    public static string NormalizePublicBaseUrl(string? publicBaseUrl)
    {
        return string.IsNullOrWhiteSpace(publicBaseUrl)
            ? LocalStorageOptions.DefaultPublicBaseUrl
            : publicBaseUrl.TrimEnd('/');
    }

    public static string BuildPublicUrl(LocalStorageOptions options, string storageFolder, string relativePath)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/').Trim('/');
        var normalizedStorageFolder = storageFolder.Trim('/');

        return $"{NormalizePublicBaseUrl(options.PublicBaseUrl)}{NormalizeRequestPath(options.RequestPath)}/{normalizedStorageFolder}/{normalizedRelativePath}";
    }

    public static string GetPhysicalPath(LocalStorageOptions options, string storageFolder, string relativePath)
    {
        var relativeSegments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        return Path.Combine([options.RootPath, storageFolder, .. relativeSegments]);
    }

    public static string? TryGetRelativePath(LocalStorageOptions options, string storageFolder, string? fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            return null;
        }

        var candidatePath = fileUrl.Trim();

        if (Uri.TryCreate(candidatePath, UriKind.Absolute, out var uri))
        {
            candidatePath = uri.AbsolutePath;
        }

        candidatePath = Uri.UnescapeDataString(candidatePath);

        if (!candidatePath.StartsWith('/'))
        {
            candidatePath = $"/{candidatePath.TrimStart('/')}";
        }

        var prefix = $"{NormalizeRequestPath(options.RequestPath)}/{storageFolder.Trim('/')}/";
        if (!candidatePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativePath = candidatePath[prefix.Length..].Trim('/');
        return string.IsNullOrWhiteSpace(relativePath) ? null : relativePath;
    }
}
