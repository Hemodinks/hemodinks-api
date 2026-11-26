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

        Assert.Equal(
            "A foto selecionada tem 2,1 MB e o limite é 2 MB. Reduza ou comprima a imagem e envie novamente.",
            exception.Message);
    }

    [Fact]
    public async Task SaveAsync_OversizedPhoto_ReportsActualSizeAndLimit()
    {
        var storage = CreateStorage(Path.Combine(Path.GetTempPath(), $"hemodinks-photo-test-{Guid.NewGuid():N}"));
        var dataUrl = CreatePngDataUrl((int)(2.4 * 1024 * 1024));

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            storage.SaveAsync(dataUrl, null, CancellationToken.None));

        Assert.StartsWith("A foto selecionada tem 2,4 MB e o limite é 2 MB.", exception.Message);
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
    [InlineData("data:image/gif;base64,R0lGODlh", "Formato de imagem não aceito. Use uma foto PNG, JPG ou WEBP.")]
    [InlineData("nao-e-data-url", "Não foi possível ler a foto enviada. Selecione a imagem novamente (PNG, JPG ou WEBP).")]
    [InlineData("data:image/png;base64,@@@", "Não foi possível ler a foto enviada. Selecione a imagem novamente (PNG, JPG ou WEBP).")]
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

    // Function com limite menor que o da API (caso real de 19/11/2026): a recusa da Function precisa chegar
    // ao usuario como regra de negocio (400 com a mensagem), nao como 500 generico.
    [Fact]
    public async Task FunctionBacked_FunctionRejectsByBusinessRule_SurfacesMessageAsBusinessRule()
    {
        const string functionMessage =
            "A foto selecionada tem 1,5 MB e o limite é 1 MB. Reduza ou comprima a imagem e envie novamente.";
        var factory = new CountingHttpClientFactory(System.Net.HttpStatusCode.BadRequest,
            $$"""{"message":"{{functionMessage}}"}""");
        var storage = CreateFunctionBackedStorage(factory);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            storage.SaveAsync(CreatePngDataUrl(1024), null, CancellationToken.None));

        Assert.Equal(functionMessage, error.Message);
        Assert.Equal(1, factory.Calls);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.InternalServerError, """{"message":"detalhe interno"}""")]
    [InlineData(System.Net.HttpStatusCode.BadRequest, "texto sem json")]
    [InlineData(System.Net.HttpStatusCode.BadRequest, """{"error":"sem campo message"}""")]
    [InlineData(System.Net.HttpStatusCode.Unauthorized, """{"message":"chave invalida"}""")]
    public async Task FunctionBacked_OtherFunctionFailures_RemainInternalErrors(
        System.Net.HttpStatusCode status, string body)
    {
        var factory = new CountingHttpClientFactory(status, body);
        var storage = CreateFunctionBackedStorage(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync(CreatePngDataUrl(1024), null, CancellationToken.None));
    }

    // #399: sem mudanca a gravar ou apagar, nao chama a Function (o cold start dela atrasava criar paciente sem foto).
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData("   ", "", null)]
    [InlineData(null, "data:image/png;base64,iVBORw0KGgo=", null)]
    [InlineData("https://storage.example/profile-photos/a.png", "https://storage.example/profile-photos/a.png",
        "https://storage.example/profile-photos/a.png")]
    public async Task FunctionBacked_NothingToStore_ReturnsDirectResultWithoutCallingFunction(
        string? requested, string? current, string? expected)
    {
        var factory = new CountingHttpClientFactory(System.Net.HttpStatusCode.InternalServerError, "nao deveria ser chamada");
        var storage = CreateFunctionBackedStorage(factory);

        var result = await storage.SaveAsync(requested, current, CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Equal(0, factory.Calls);
    }

    // Upload novo e remocao de foto existente continuam pela Function.
    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "https://storage.example/profile-photos/a.png")]
    public async Task FunctionBacked_UploadOrRemovingStoredPhoto_StillCallsFunction(bool upload, string? current)
    {
        var factory = new CountingHttpClientFactory(System.Net.HttpStatusCode.OK, """{"fotoPerfil":"https://storage.example/novo.png"}""");
        var storage = CreateFunctionBackedStorage(factory);

        await storage.SaveAsync(upload ? CreatePngDataUrl(1024) : null, current, CancellationToken.None);

        Assert.Equal(1, factory.Calls);
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

    private sealed class CountingHttpClientFactory(
        System.Net.HttpStatusCode status = System.Net.HttpStatusCode.BadRequest,
        string body = "") : IHttpClientFactory
    {
        public int Calls { get; private set; }

        public HttpClient CreateClient(string name)
        {
            Calls++;
            return new HttpClient(new RejectingHandler(status, body));
        }

        private sealed class RejectingHandler(System.Net.HttpStatusCode status, string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
