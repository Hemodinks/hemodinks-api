using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Licencas;

internal static class LicencaValueParser
{
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;

    public static string NormalizePlano(string value)
    {
        if (TextComparer.Equals(value, LicencaPlanos.Trial))
        {
            return LicencaPlanos.Trial;
        }

        if (TextComparer.Equals(value, LicencaPlanos.Completa) || TextComparer.Equals(value, "Full"))
        {
            return LicencaPlanos.Completa;
        }

        throw new InvalidOperationException("Plano de licenca invalido");
    }

    public static string NormalizeStatus(string value)
    {
        if (TextComparer.Equals(value, LicencaStatus.Ativa))
        {
            return LicencaStatus.Ativa;
        }

        if (TextComparer.Equals(value, LicencaStatus.Suspensa))
        {
            return LicencaStatus.Suspensa;
        }

        if (TextComparer.Equals(value, LicencaStatus.Cancelada))
        {
            return LicencaStatus.Cancelada;
        }

        throw new InvalidOperationException("Status de licenca invalido");
    }

    public static string? SerializeFeatures(IEnumerable<string> features)
    {
        var normalized = features
            .Select(TrimToNull)
            .Where(feature => feature != null)
            .Select(feature => feature!)
            .Distinct(TextComparer)
            .ToList();

        var invalidFeature = normalized
            .FirstOrDefault(feature => !LicencaFeatures.Todas.Contains(feature, TextComparer));

        if (invalidFeature != null)
        {
            throw new InvalidOperationException($"Feature de licenca invalida: {invalidFeature}");
        }

        return normalized.Count == 0 ? null : string.Join(';', normalized);
    }

    public static IReadOnlyList<string> ParseFeatures(string? features)
    {
        if (string.IsNullOrWhiteSpace(features))
        {
            return [];
        }

        return features
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(TextComparer)
            .ToList();
    }

    public static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
