using System.Globalization;
using System.Security.Cryptography;

namespace HemodinksAPI.Tests;

internal static class PasswordHashTestData
{
    public static string Create(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256,
            iterations == 10_000 ? 20 : 32);
        return iterations == 10_000 ? Convert.ToBase64String([.. salt, .. key])
            : string.Join('$', "PBKDF2-SHA256", iterations.ToString(CultureInfo.InvariantCulture),
                Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }
}
