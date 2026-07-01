using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Api;

internal static class RequestIdempotencySupport
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string? ValidateKey(string? key)
    {
        var normalized = NormalizeKey(key);
        if (normalized.Length == 0)
        {
            return "O header Idempotency-Key nao pode ser vazio.";
        }

        if (normalized.Length > 200)
        {
            return "O header Idempotency-Key deve ter no maximo 200 caracteres.";
        }

        return null;
    }

    public static string ComputeHash(object payload)
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    public static void MarkAsCompleted<TResponse>(
        IdempotencyRequest record,
        TResponse payload,
        string? resourceLocation,
        int successStatusCode)
    {
        record.State = IdempotencyRequestStates.Completed;
        record.StatusCode = successStatusCode;
        record.ResourceLocation = resourceLocation;
        record.ResponseJson = JsonSerializer.Serialize(payload, SerializerOptions);
        record.CompletedAt = DateTime.UtcNow;
    }

    public static TResponse? DeserializePayload<TResponse>(string responseJson)
    {
        return JsonSerializer.Deserialize<TResponse>(responseJson, SerializerOptions);
    }

    public static string NormalizeKey(string? key)
    {
        return key?.Trim() ?? string.Empty;
    }

    public static string NormalizeScope(string? scope)
    {
        return string.IsNullOrWhiteSpace(scope)
            ? "anonymous"
            : scope.Trim();
    }
}
