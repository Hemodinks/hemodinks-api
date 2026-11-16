using HemodinksAPI.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HemodinksAPI.Domain.Exceptions;

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

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
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

    // #293: o caminho de producao (Function) precisa rejeitar com regra de negocio (400) antes do POST;
    // erro devolvido pela Function seria falha interna (500).
    [Theory]
    [InlineData("data:image/gif;base64,R0lGODlh", "Use uma foto PNG, JPG ou WEBP")]
    [InlineData("nao-e-data-url", "Foto de perfil invalida")]
    [InlineData("data:image/png;base64,@@@", "Foto de perfil invalida")]
    public async Task FunctionBacked_InvalidPhoto_IsRejectedBeforeCallingFunction(string photo, string message)
    {
        var factory = new CountingHttpClientFactory();
        var storage = CreateFunctionBackedStorage(factory);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            storage.SaveAsync(photo, null, CancellationToken.None));

        Assert.Equal(message, error.Message);
        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public async Task FunctionBacked_OversizedPhoto_IsRejectedBeforeCallingFunction()
    {
        var factory = new CountingHttpClientFactory();
        var storage = CreateFunctionBackedStorage(factory);

        await Assert.ThrowsAsync<BusinessRuleException>(() => storage.SaveAsync(
            CreatePngDataUrl((int)ProfilePhotoStorageOptions.DefaultMaxBytes + 1), null, CancellationToken.None));

        Assert.Equal(0, factory.Calls);
    }

    private static FunctionBackedProfilePhotoStorage CreateFunctionBackedStorage(CountingHttpClientFactory factory)
    {
        var photoOptions = Options.Create(new ProfilePhotoStorageOptions());
        return new FunctionBackedProfilePhotoStorage(
            new StorageFunctionClient(factory, Options.Create(new StorageFunctionOptions
            {
                BaseUrl = "https://functions.invalid/api/",
                FunctionKey = "test-key"
            })),
            new AzureBlobProfilePhotoStorage(photoOptions, NullLogger<AzureBlobProfilePhotoStorage>.Instance),
            photoOptions);
    }

    private sealed class CountingHttpClientFactory : IHttpClientFactory
    {
        public int Calls { get; private set; }

        public HttpClient CreateClient(string name)
        {
            Calls++;
            return new HttpClient(new RejectingHandler());
        }

        private sealed class RejectingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
        }
    }
}
