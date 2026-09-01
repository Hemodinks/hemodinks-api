using System.Reflection;
using HemodinksAPI.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public class AzureBlobPatientFileStorageTests
{
    [Theory]
    [InlineData("pacientes/2026/09/file.pdf", "patient-files-confirmation", "pacientes/2026/09/file.pdf")]
    [InlineData("https://stgmtechsolution.blob.core.windows.net/patient-files-confirmation/pacientes/2026/09/file.pdf", "patient-files-confirmation", "pacientes/2026/09/file.pdf")]
    [InlineData("https://stgmtechsolution.blob.core.windows.net/patient-files/pacientes/2026/09/file.pdf", "patient-files-confirmation", "pacientes/2026/09/file.pdf")]
    [InlineData("https://stgmtechsolution.blob.core.windows.net/patient-files/patient-files-confirmation/pacientes/2026/09/file.pdf", "patient-files-confirmation", "pacientes/2026/09/file.pdf")]
    [InlineData("http://azurite:10000/devstoreaccount1/patient-files/pacientes/2026/09/file.pdf", "patient-files-confirmation", "pacientes/2026/09/file.pdf")]
    public void GetBlobLocationFromUrl_ResolvesCurrentLegacyAndNestedContainerPaths(
        string fileUrl,
        string expectedContainerName,
        string expectedBlobName)
    {
        var storage = new AzureBlobPatientFileStorage(
            Options.Create(new PatientFileStorageOptions
            {
                ContainerName = "patient-files-confirmation",
                PublicBaseUrl = "https://stgmtechsolution.blob.core.windows.net/patient-files-confirmation"
            }),
            NullLogger<AzureBlobPatientFileStorage>.Instance);

        var location = ResolveBlobLocation(storage, fileUrl);

        Assert.NotNull(location);
        Assert.Equal(expectedContainerName, location.Value.ContainerName);
        Assert.Equal(expectedBlobName, location.Value.BlobName);
    }

    [Theory]
    [InlineData("https://example.com/other-container/pacientes/file.pdf")]
    [InlineData("https://example.com/patient-files-confirmation/users/file.pdf")]
    [InlineData("invalid")]
    public void GetBlobLocationFromUrl_RejectsUnsupportedPaths(string fileUrl)
    {
        var storage = new AzureBlobPatientFileStorage(
            Options.Create(new PatientFileStorageOptions
            {
                ContainerName = "patient-files-confirmation"
            }),
            NullLogger<AzureBlobPatientFileStorage>.Instance);

        Assert.Null(ResolveBlobLocation(storage, fileUrl));
    }

    private static (string ContainerName, string BlobName)? ResolveBlobLocation(
        AzureBlobPatientFileStorage storage,
        string fileUrl)
    {
        var method = typeof(AzureBlobPatientFileStorage).GetMethod(
            "GetBlobLocationFromUrl",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var location = method?.Invoke(storage, [fileUrl]);

        if (location == null)
        {
            return null;
        }

        var locationType = location.GetType();
        return (
            (string)locationType.GetProperty("ContainerName")!.GetValue(location)!,
            (string)locationType.GetProperty("BlobName")!.GetValue(location)!);
    }
}
