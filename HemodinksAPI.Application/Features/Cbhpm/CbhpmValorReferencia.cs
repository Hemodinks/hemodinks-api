namespace HemodinksAPI.Application.Features.Cbhpm;

internal static class CbhpmValorReferencia
{
    private const decimal Uco2012 = 14.33m;

    private static readonly IReadOnlyDictionary<string, decimal> ValoresPorte2012 =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["1A"] = 12.86m,
            ["1B"] = 25.72m,
            ["1C"] = 38.58m,
            ["2A"] = 51.45m,
            ["2B"] = 67.82m,
            ["2C"] = 80.26m,
            ["3A"] = 109.67m,
            ["3B"] = 140.14m,
            ["3C"] = 160.52m,
            ["4A"] = 191.04m,
            ["4B"] = 209.13m,
            ["4C"] = 236.26m,
            ["5A"] = 254.34m,
            ["5B"] = 274.69m,
            ["5C"] = 291.64m,
            ["6A"] = 317.65m,
            ["6B"] = 349.30m,
            ["6C"] = 382.08m,
            ["7A"] = 412.60m,
            ["7B"] = 456.68m,
            ["7C"] = 540.33m,
            ["8A"] = 583.29m,
            ["8B"] = 611.55m,
            ["8C"] = 648.85m,
            ["9A"] = 689.55m,
            ["9B"] = 753.99m,
            ["9C"] = 830.84m,
            ["10A"] = 891.89m,
            ["10B"] = 966.50m,
            ["10C"] = 1072.75m,
            ["11A"] = 1134.93m,
            ["11B"] = 1244.58m,
            ["11C"] = 1365.54m,
            ["12A"] = 1415.27m,
            ["12B"] = 1521.53m,
            ["12C"] = 1864.04m,
            ["13A"] = 2051.69m,
            ["13B"] = 2250.64m,
            ["13C"] = 2489.16m,
            ["14A"] = 2774.02m,
            ["14B"] = 3018.19m,
            ["14C"] = 3329.05m
        };

    public static decimal? Calcular(string? porte, decimal? custoOperacional)
    {
        if (string.IsNullOrWhiteSpace(porte)
            || !ValoresPorte2012.TryGetValue(porte.Trim(), out var valorPorte))
        {
            return null;
        }

        return Math.Round(
            valorPorte + ((custoOperacional ?? 0m) * Uco2012),
            2,
            MidpointRounding.AwayFromZero);
    }
}
