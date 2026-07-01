using HemodinksAPI.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed class LocalDiskPatientFileStorageTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "hemodinks-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_StoresFileOnDiskAndDeleteAsync_RemovesIt()
    {
        var storage = new LocalDiskPatientFileStorage(
            Options.Create(new PatientFileStorageOptions { MaxBytes = 1024 * 1024 }),
            Options.Create(new LocalStorageOptions
            {
                RootPath = _rootPath,
                RequestPath = "/storage",
                PublicBaseUrl = "http://localhost:5000"
            }),
            NullLogger<LocalDiskPatientFileStorage>.Instance);

        var file = new FormFile(new MemoryStream("conteudo"u8.ToArray()), 0, 8, "file", "laudo.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var storedFile = await storage.SaveAsync(file, CancellationToken.None);
        var localPath = GetLocalPathFromUrl(storedFile.Url);

        Assert.Equal("laudo.pdf", storedFile.OriginalName);
        Assert.Equal("application/pdf", storedFile.ContentType);
        Assert.StartsWith("http://localhost:5000/storage/patient-files/pacientes/", storedFile.Url);
        Assert.True(File.Exists(localPath));

        await storage.DeleteAsync(storedFile.Url, CancellationToken.None);

        Assert.False(File.Exists(localPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private string GetLocalPathFromUrl(string url)
    {
        var uri = new Uri(url);
        var relativePath = uri.AbsolutePath["/storage/patient-files/".Length..]
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.Combine(_rootPath, "patient-files", relativePath);
    }
}
