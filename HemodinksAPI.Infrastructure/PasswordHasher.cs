using System.Security.Cryptography;

namespace HemodinksAPI.Infrastructure.Utils;

/// <summary>
/// Implementação do hash de senha usando PBKDF2
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const string CurrentFormat = "PBKDF2-SHA256";
    private const int SaltSize = 16;
    private const int LegacyHashSize = 20;
    private const int HashSize = 32;
    private const int Iterations = 210000;
    private const int LegacyIterations = 10000;

    public string HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);

        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return string.Join(
            '$',
            CurrentFormat,
            Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(saltBytes),
            Convert.ToBase64String(hashBytes));
    }

    public bool VerifyPassword(string password, string hash)
    {
        try
        {
            if (hash.StartsWith($"{CurrentFormat}$", StringComparison.Ordinal))
            {
                return VerifyCurrentHash(password, hash);
            }

            return VerifyLegacyHash(password, hash);
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyCurrentHash(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4
            || parts[0] != CurrentFormat
            || !int.TryParse(parts[1], out var iterations)
            || iterations < LegacyIterations)
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static bool VerifyLegacyHash(string password, string storedHash)
    {
        var hashBytes = Convert.FromBase64String(storedHash);

        if (hashBytes.Length != SaltSize + LegacyHashSize)
        {
            return false;
        }

        var salt = new byte[SaltSize];
        Array.Copy(hashBytes, 0, salt, 0, SaltSize);

        var expectedHash = new byte[LegacyHashSize];
        Array.Copy(hashBytes, SaltSize, expectedHash, 0, LegacyHashSize);

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            LegacyIterations,
            HashAlgorithmName.SHA256,
            LegacyHashSize);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
