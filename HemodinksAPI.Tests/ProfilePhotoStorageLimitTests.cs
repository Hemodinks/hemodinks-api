using HemodinksAPI.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed class ProfilePhotoStorageLimitTests
{
    [Fact]
    public async Task SaveAsync_AcceptsPhotoWithExactlyTwoMegabytes()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"hemodinks-photo-test-{Guid.NewGuid():N}");

        try
        {
            var storage = CreateStorage(rootPath);
            var dataUrl = CreatePngDataUrl((int)ProfilePhotoStorageOptions.DefaultMaxBytes);

            var result = await storage.SaveAsync(dataUrl, null, CancellationToken.None);

            Assert.NotNull(result);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsPhotoLargerThanTwoMegabytes()
    {
        var storage = CreateStorage(Path.Combine(Path.GetTempPath(), $"hemodinks-photo-test-{Guid.NewGuid():N}"));
        var dataUrl = CreatePngDataUrl((int)ProfilePhotoStorageOptions.DefaultMaxBytes + 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync(dataUrl, null, CancellationToken.None));

        Assert.Equal("A foto deve ter no maximo 2 MB", exception.Message);
    }

    private static LocalDiskProfilePhotoStorage CreateStorage(string rootPath)
    {
        return new LocalDiskProfilePhotoStorage(
            Options.Create(new ProfilePhotoStorageOptions()),
            Options.Create(new LocalStorageOptions { RootPath = rootPath }),
            NullLogger<LocalDiskProfilePhotoStorage>.Instance);
    }

    private static string CreatePngDataUrl(int sizeBytes)
    {
        return $"data:image/png;base64,{Convert.ToBase64String(new byte[sizeBytes])}";
    }
}
