using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.PasswordReset;

public class PasswordResetFunctionClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PasswordResetFunctionOptions _options;

    public PasswordResetFunctionClient(
        IHttpClientFactory httpClientFactory,
        IOptions<PasswordResetFunctionOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task PostJsonAsync<TRequest>(
        string relativePath,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(nameof(PasswordResetFunctionClient));
        client.BaseAddress = BuildBaseUri();

        var functionKey = _options.FunctionKey?.Trim();
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            throw new InvalidOperationException(
                "PasswordResetFunctions:FunctionKey deve ser configurado para envio de reset via Azure Functions.");
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
                $"Falha ao chamar a password reset function ({(int)response.StatusCode} {response.ReasonPhrase}): {errorBody}");
        }
    }

    private Uri BuildBaseUri()
    {
        var baseUrl = _options.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "PasswordResetFunctions:BaseUrl deve ser configurado para envio de reset via Azure Functions.");
        }

        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        normalizedBaseUrl = normalizedBaseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase)
            ? $"{normalizedBaseUrl}/"
            : $"{normalizedBaseUrl}/api/";

        return Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException("PasswordResetFunctions:BaseUrl deve ser uma URL absoluta valida.");
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.TrimStart('/');
    }
}
