using System.Globalization;
using System.Security.Cryptography;

namespace HemodinksAPI.Infrastructure.Utils;

public class PasswordHasher : IPasswordHasher
{
    private const string CurrentFormat = "PBKDF2-SHA256";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int LegacyHashSize = 20;
    private const int LegacyIterations = 10_000;
    private const int Iterations = 600_000;
    // Bound work from stored input while accepting stronger credentials without downgrading.
    private const int MaximumIterations = 2_000_000;
    private const int MaximumEncodedLength = 128;

    public string HashPassword(string password) => HashWithIterations(password, Iterations);

    internal static string HashWithIterations(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', CurrentFormat, iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(string password, string hash) =>
        VerifyPasswordWithRehash(password, hash) != PasswordVerificationResult.Failed;

    public PasswordVerificationResult VerifyPasswordWithRehash(string password, string hash) =>
        VerifyCore(password, hash);

    internal static PasswordVerificationResult VerifyCore(string password, string hash)
    {
        if (password is null || hash is null || hash.Length > MaximumEncodedLength)
            return PasswordVerificationResult.Failed;

        if (!hash.StartsWith(CurrentFormat + "$", StringComparison.Ordinal))
            return VerifyLegacyHash(password, hash);

        var parts = hash.Split('$');
        if (parts.Length != 4
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < LegacyIterations || iterations > MaximumIterations
            || parts[2].Length != 24 || parts[3].Length != 44)
            return PasswordVerificationResult.Failed;

        Span<byte> salt = stackalloc byte[SaltSize];
        Span<byte> expected = stackalloc byte[HashSize];
        if (!Convert.TryFromBase64String(parts[2], salt, out var saltLength) || saltLength != SaltSize
            || !Convert.TryFromBase64String(parts[3], expected, out var hashLength) || hashLength != HashSize)
            return PasswordVerificationResult.Failed;

        Span<byte> actual = stackalloc byte[HashSize];
        Rfc2898DeriveBytes.Pbkdf2(password.AsSpan(), salt, actual, iterations, HashAlgorithmName.SHA256);
        var valid = CryptographicOperations.FixedTimeEquals(actual, expected);
        CryptographicOperations.ZeroMemory(actual);
        return !valid ? PasswordVerificationResult.Failed
            : iterations < Iterations ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    private static PasswordVerificationResult VerifyLegacyHash(string password, string hash)
    {
        if (hash.Length != 48) return PasswordVerificationResult.Failed;
        Span<byte> decoded = stackalloc byte[SaltSize + LegacyHashSize];
        if (!Convert.TryFromBase64String(hash, decoded, out var size) || size != decoded.Length)
            return PasswordVerificationResult.Failed;

        Span<byte> actual = stackalloc byte[LegacyHashSize];
        Rfc2898DeriveBytes.Pbkdf2(password.AsSpan(), decoded[..SaltSize], actual,
            LegacyIterations, HashAlgorithmName.SHA256);
        var valid = CryptographicOperations.FixedTimeEquals(actual, decoded[SaltSize..]);
        CryptographicOperations.ZeroMemory(actual);
        return valid ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Failed;
    }
}
