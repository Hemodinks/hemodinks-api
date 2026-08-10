using System.Text;

namespace HemodinksAPI.Application.Features.Common;

public static class FullTextSearchTermBuilder
{
    public const int MinimumTermLength = 2;

    /// <summary>
    /// Converte texto livre em uma condicao CONTAINS segura de prefixos, por exemplo
    /// <c>"cirurg*" AND "cardiac*"</c>. Pontuacao e operadores sao tratados como
    /// separadores; somente letras, digitos e marcas Unicode compoem os termos.
    /// </summary>
    public static string? BuildPrefixCondition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var terms = Tokenize(value)
            .Where(term => term.Length >= MinimumTermLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(term => $"\"{term}*\"")
            .ToArray();

        return terms.Length == 0 ? null : string.Join(" AND ", terms);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var current = new StringBuilder();

        foreach (var character in value.Trim())
        {
            var category = char.GetUnicodeCategory(character);
            if (char.IsLetterOrDigit(character)
                || category is System.Globalization.UnicodeCategory.NonSpacingMark
                    or System.Globalization.UnicodeCategory.SpacingCombiningMark)
            {
                current.Append(character);
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
