using System.Globalization;

namespace HemodinksAPI.Application.Features.Financeiro;

public static class LegacyFinanceiroFallback
{
    public static bool TryParseCurrency(string? value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u00a0", string.Empty).Replace(" ", string.Empty);
        if (normalized.Contains(',')) normalized = normalized.Replace(".", string.Empty).Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount >= 0;
    }
}
