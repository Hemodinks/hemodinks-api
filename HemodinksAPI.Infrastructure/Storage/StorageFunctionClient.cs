using System.Net.Http.Json;
using System.Text;
using HemodinksAPI.Domain.Exceptions;
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

        await EnsureSuccessAsync(response, cancellationToken);

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
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, NormalizeRelativePath(relativePath));
        request.Headers.Add("X-File-Name-Base64", Convert.ToBase64String(Encoding.UTF8.GetBytes(fileName)));
        foreach (var (name, value) in extraHeaders ?? new Dictionary<string, string>())
        {
            request.Headers.Add(name, value);
        }
        request.Headers.Add("X-File-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        request.Content.Headers.ContentLength = length;

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException("A storage function retornou uma resposta vazia.");
    }

    // 400 com { message } e recusa por regra de negocio da Function (tamanho, formato): vira BusinessRuleException
    // para a API responder 400 com a mensagem. Qualquer outra falha continua erro interno (500 generico ao usuario).
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest
            && TryReadBusinessMessage(errorBody) is { } businessMessage)
        {
            throw new BusinessRuleException(businessMessage);
        }

        throw new InvalidOperationException(
            $"Falha ao chamar a storage function ({(int)response.StatusCode} {response.ReasonPhrase}): {errorBody}");
    }

    private static string? TryReadBusinessMessage(string body)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == System.Text.Json.JsonValueKind.String
                && !string.IsNullOrWhiteSpace(message.GetString())
                ? message.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
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
