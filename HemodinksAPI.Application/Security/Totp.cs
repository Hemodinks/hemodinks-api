using System.Security.Cryptography;
using System.Text;

namespace HemodinksAPI.Application.Security;

/// <summary>
/// #150: TOTP (RFC 6238) sobre HMAC-SHA1 da BCL, compativel com Google/Microsoft Authenticator.
/// Nao e criptografia propria: so o algoritmo padronizado (RFC 4226/6238) com primitivas do .NET.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;
    public const int SecretBytes = 20;

    // Tolerancia de relogio: um passo antes e um depois (+-30 s).
    private const int AllowedDriftSteps = 1;

    public static string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    public static long CurrentStep(DateTimeOffset now) => now.ToUnixTimeSeconds() / StepSeconds;

    public static string ComputeCode(byte[] secret, long step, int digits = Digits)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var index = 7; index >= 0; index--)
        {
            counter[index] = (byte)(step & 0xFF);
            step >>= 8;
        }

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var modulo = (int)Math.Pow(10, digits);
        return (binary % modulo).ToString(new string('0', digits));
    }

    /// <summary>
    /// Valida o codigo na janela de +-1 passo e recusa passos ja usados (anti-replay). Comparacao em tempo constante.
    /// </summary>
    public static bool TryVerify(string base32Secret, string? code, DateTimeOffset now, long? lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        var normalized = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        if (normalized.Length != Digits)
        {
            return false;
        }

        var secret = Base32.Decode(base32Secret);
        var current = CurrentStep(now);
        var expected = Encoding.ASCII.GetBytes(normalized);
        for (var step = current - AllowedDriftSteps; step <= current + AllowedDriftSteps; step++)
        {
            if (lastUsedStep.HasValue && step <= lastUsedStep.Value)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ComputeCode(secret, step)), expected))
            {
                matchedStep = step;
                return true;
            }
        }

        return false;
    }

    public static string BuildOtpAuthUri(string issuer, string accountName, string base32Secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}"
        + $"?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}

/// <summary>Base32 RFC 4648 (alfabeto A-Z2-7, sem padding), formato dos aplicativos autenticadores.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    public static byte[] Decode(string value)
    {
        var cleaned = value.Trim().TrimEnd('=').Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var output = new List<byte>(cleaned.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var character in cleaned)
        {
            var index = Alphabet.IndexOf(character);
            if (index < 0)
            {
                throw new FormatException("Segredo TOTP invalido.");
            }

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
