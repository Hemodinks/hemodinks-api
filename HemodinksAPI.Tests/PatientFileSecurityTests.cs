using System.IO.Compression;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed class PatientFileSecurityTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "hemodinks-upload-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_WhenPdfExtensionContainsArbitraryContent_RejectsUpload()
    {
        var content = "not-a-pdf"u8.ToArray();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateStorage().SaveAsync(CreateFile("laudo.pdf", content), CancellationToken.None));

        Assert.Contains("não corresponde", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_WhenActualContentExceedsLimit_RejectsEvenIfDeclaredLengthIsSmaller()
    {
        var content = new byte[2048];
        "%PDF-"u8.CopyTo(content);
        var file = new UploadedFile("laudo.pdf", "application/pdf", 10,
            () => new MemoryStream(content, writable: false));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateStorage(maxBytes: 1024).SaveAsync(file, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_WhenDocxZipDoesNotContainWordPackage_RejectsUpload()
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var entry = archive.CreateEntry("unrelated.txt").Open();
            await entry.WriteAsync("content"u8.ToArray());
        }

        var bytes = content.ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateStorage().SaveAsync(CreateFile("report.docx", bytes), CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_StripsClientPathFromOriginalFileName()
    {
        var content = "%PDF-1.7\ncontent\n%%EOF"u8.ToArray();

        var stored = await CreateStorage().SaveAsync(
            CreateFile("../../sensitive/laudo.pdf", content),
            CancellationToken.None);

        Assert.Equal("laudo.pdf", stored.OriginalName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private LocalDiskPatientFileStorage CreateStorage(long maxBytes = 1024 * 1024) => new(
        Options.Create(new PatientFileStorageOptions { MaxBytes = maxBytes }),
        Options.Create(new LocalStorageOptions { RootPath = _rootPath, RequestPath = "/storage" }),
        NullLogger<LocalDiskPatientFileStorage>.Instance);

    private static UploadedFile CreateFile(string name, byte[] content) =>
        new(name, "application/octet-stream", content.LongLength,
            () => new MemoryStream(content, writable: false));
}
