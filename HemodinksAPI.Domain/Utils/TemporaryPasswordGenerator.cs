using System.Security.Cryptography;

namespace HemodinksAPI.Domain.Utils;

/// <summary>
/// Gera credenciais temporárias únicas para o primeiro acesso e resets administrativos.
/// </summary>
public static class TemporaryPasswordGenerator
{
    private const string Alphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";

    public static string Generate()
    {
        Span<char> password = stackalloc char[20];
        password[0] = "ABCDEFGHJKLMNPQRSTUVWXYZ"[RandomNumberGenerator.GetInt32(23)];
        password[1] = "abcdefghijkmnopqrstuvwxyz"[RandomNumberGenerator.GetInt32(25)];
        password[2] = "23456789"[RandomNumberGenerator.GetInt32(8)];
        password[3] = "!@#$%"[RandomNumberGenerator.GetInt32(5)];

        for (var index = 4; index < password.Length; index++)
        {
            password[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        RandomNumberGenerator.Shuffle(password);
        return new string(password);
    }
}
