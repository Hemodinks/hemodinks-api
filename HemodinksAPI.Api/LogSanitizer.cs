using System.Text.RegularExpressions;

namespace HemodinksAPI.Api;

// #328: valores vindos da requisição (método, caminho) entram no log sem caracteres de controle nem
// separadores de linha, para que um %0A no caminho não forje uma linha nova no console ({Message:lj}).
internal static partial class LogSanitizer
{
    public static string Clean(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : LineBreaksAndControlCharacters().Replace(value, "?");

    [GeneratedRegex(@"[\p{Cc}\p{Zl}\p{Zp}]")]
    private static partial Regex LineBreaksAndControlCharacters();
}
