using System.Reflection;
using HemodinksAPI.Api.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public class AzureBlobProfilePhotoStorageTests
{
    [Theory]
    [InlineData("/profile-photos/george.png", "profile-photos", "george.png")]
    [InlineData("https://stgmtechsolution.blob.core.windows.net/profile-photos/george.png", "profile-photos", "george.png")]
    [InlineData("users/2026/06/avatar.png", "profile-photos-confirmation", "users/2026/06/avatar.png")]
    [InlineData("https://stgmtechsolution.blob.core.windows.net/profile-photos-confirmation/users/2026/06/avatar.png", "profile-photos-confirmation", "users/2026/06/avatar.png")]
    public void GetBlobLocationFromUrl_ResolvesCurrentAndLegacyProfilePhotoPaths(
        string fotoPerfil,
        string expectedContainerName,
        string expectedBlobName)
    {
        var storage = new AzureBlobProfilePhotoStorage(
            Options.Create(new ProfilePhotoStorageOptions
            {
                ContainerName = "profile-photos-confirmation",
                PublicBaseUrl = "https://stgmtechsolution.blob.core.windows.net/profile-photos-confirmation"
            }),
            NullLogger<AzureBlobProfilePhotoStorage>.Instance);

        var location = ResolveBlobLocation(storage, fotoPerfil);

        Assert.NotNull(location);
        Assert.Equal(expectedContainerName, location.Value.ContainerName);
        Assert.Equal(expectedBlobName, location.Value.BlobName);
    }

    private static (string ContainerName, string BlobName)? ResolveBlobLocation(
        AzureBlobProfilePhotoStorage storage,
        string fotoPerfil)
    {
        var method = typeof(AzureBlobProfilePhotoStorage).GetMethod(
            "GetBlobLocationFromUrl",
            BindingFlags.Instance | BindingFlags.NonPublic);

        var location = method?.Invoke(storage, [fotoPerfil]);

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
