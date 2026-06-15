namespace HemodinksAPI.Application.Features.Cbhpm;

public static class CbhpmCodigoUtils
{
    public static string Normalize(string? codigo)
    {
        return string.IsNullOrWhiteSpace(codigo)
            ? string.Empty
            : new string(codigo.Where(char.IsDigit).ToArray());
    }

    public static string? NormalizeOptional(string? codigo)
    {
        var normalized = Normalize(codigo);
        return normalized.Length == 0 ? null : normalized;
    }

    public static bool ContainsNormalizedOrOriginal(string codigo, string value)
    {
        if (codigo.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedValue = NormalizeOptional(value);
        return normalizedValue != null
            && Normalize(codigo).Contains(normalizedValue, StringComparison.OrdinalIgnoreCase);
    }
}
