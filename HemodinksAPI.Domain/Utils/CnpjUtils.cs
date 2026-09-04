namespace HemodinksAPI.Domain.Utils;

public static class CnpjUtils
{
    private static readonly int[] FirstDigitWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] SecondDigitWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new string(value.Where(character => character is >= '0' and <= '9').ToArray());
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => character is not (>= '0' and <= '9')
                && character is not '.' and not '/' and not '-'
                && !char.IsWhiteSpace(character)))
        {
            return false;
        }

        var cnpj = Normalize(value);
        if (cnpj is null || cnpj.Length != 14 || cnpj.Distinct().Count() == 1)
        {
            return false;
        }

        return CalculateDigit(cnpj, FirstDigitWeights) == cnpj[12] - '0'
            && CalculateDigit(cnpj, SecondDigitWeights) == cnpj[13] - '0';
    }

    private static int CalculateDigit(string cnpj, IReadOnlyList<int> weights)
    {
        var sum = 0;
        for (var index = 0; index < weights.Count; index++)
        {
            sum += (cnpj[index] - '0') * weights[index];
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
