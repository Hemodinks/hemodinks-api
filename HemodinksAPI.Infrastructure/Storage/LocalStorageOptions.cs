namespace HemodinksAPI.Infrastructure.Storage;

public class LocalStorageOptions
{
    public const string DefaultRootPath = "local-storage";
    public const string DefaultRequestPath = "/storage";
    public const string DefaultPublicBaseUrl = "http://localhost:5000";

    public string RootPath { get; set; } = DefaultRootPath;

    public string RequestPath { get; set; } = DefaultRequestPath;

    public string PublicBaseUrl { get; set; } = DefaultPublicBaseUrl;
}
