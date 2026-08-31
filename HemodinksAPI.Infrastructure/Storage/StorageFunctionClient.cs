using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Storage;

public class StorageFunctionClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly StorageFunctionOptions _options;

    public StorageFunctionClient(
        IHttpClientFactory httpClientFactory,
        IOptions<StorageFunctionOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string relativePath,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(nameof(StorageFunctionClient));
        client.BaseAddress = BuildBaseUri();

        var functionKey = _options.FunctionKey?.Trim();
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            throw new InvalidOperationException(
                "StorageFunctions:FunctionKey deve ser configurado para uploads via Azure Functions.");
        }

        client.DefaultRequestHeaders.Remove("x-functions-key");
        client.DefaultRequestHeaders.Add("x-functions-key", functionKey);

        using var response = await client.PostAsJsonAsync(
            NormalizeRelativePath(relativePath),
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Falha ao chamar a storage function ({(int)response.StatusCode} {response.ReasonPhrase}): {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        if (result == null)
        {
            throw new InvalidOperationException("A storage function retornou uma resposta vazia.");
        }

        return result;
    }

    public async Task<TResponse> PostFileAsync<TResponse>(
        string relativePath,
        string fileName,
        string contentType,
        long length,
        Stream content,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, NormalizeRelativePath(relativePath));
        request.Headers.Add("X-File-Name-Base64", Convert.ToBase64String(Encoding.UTF8.GetBytes(fileName)));
        request.Headers.Add("X-File-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        request.Content.Headers.ContentLength = length;

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Falha ao chamar a storage function ({(int)response.StatusCode} {response.ReasonPhrase}): {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException("A storage function retornou uma resposta vazia.");
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(StorageFunctionClient));
        client.BaseAddress = BuildBaseUri();

        var functionKey = _options.FunctionKey?.Trim();
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            client.Dispose();
            throw new InvalidOperationException(
                "StorageFunctions:FunctionKey deve ser configurado para uploads via Azure Functions.");
        }

        client.DefaultRequestHeaders.Remove("x-functions-key");
        client.DefaultRequestHeaders.Add("x-functions-key", functionKey);
        return client;
    }

    private Uri BuildBaseUri()
    {
        var baseUrl = _options.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "StorageFunctions:BaseUrl deve ser configurado para uploads via Azure Functions.");
        }

        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        normalizedBaseUrl = normalizedBaseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase)
            ? $"{normalizedBaseUrl}/"
            : $"{normalizedBaseUrl}/api/";

        return Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException("StorageFunctions:BaseUrl deve ser uma URL absoluta valida.");
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.TrimStart('/');
    }
}
