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
        password[0] = 'A';
        password[1] = 'a';
        password[2] = '2';
        password[3] = '!';

        for (var index = 4; index < password.Length; index++)
        {
            password[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(password);
    }
}
