using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using System.Security.Cryptography;

namespace HemodinksAPI.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_WhenPasswordIsValid_ReturnsVerifiableHash()
    {
        var hash = _hasher.HashPassword("Senha@123");

        Assert.StartsWith("PBKDF2-SHA256$", hash);
        Assert.True(_hasher.VerifyPassword("Senha@123", hash));
    }

    [Fact]
    public void HashPassword_WhenCalledTwiceForSamePassword_ReturnsDifferentHashes()
    {
        var firstHash = _hasher.HashPassword("Senha@123");
        var secondHash = _hasher.HashPassword("Senha@123");

        Assert.NotEqual(firstHash, secondHash);
        Assert.True(_hasher.VerifyPassword("Senha@123", firstHash));
        Assert.True(_hasher.VerifyPassword("Senha@123", secondHash));
    }

    [Fact]
    public void VerifyPassword_WhenPasswordDoesNotMatch_ReturnsFalse()
    {
        var hash = _hasher.HashPassword("Senha@123");

        Assert.False(_hasher.VerifyPassword("Senha@456", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("U2VuaGFA")]
    public void VerifyPassword_WhenHashIsInvalid_ReturnsFalse(string invalidHash)
    {
        Assert.False(_hasher.VerifyPassword("Senha@123", invalidHash));
    }

    [Fact]
    public void VerifyPassword_WhenHashUsesLegacyFormat_ReturnsTrue()
    {
        var legacyHash = CreateLegacyHash("Senha@123");

        Assert.True(_hasher.VerifyPassword("Senha@123", legacyHash));
        Assert.False(_hasher.VerifyPassword("Senha@456", legacyHash));
    }

    private static string CreateLegacyHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            10000,
            HashAlgorithmName.SHA256,
            20);

        var hashWithSalt = new byte[salt.Length + hash.Length];
        Array.Copy(salt, 0, hashWithSalt, 0, salt.Length);
        Array.Copy(hash, 0, hashWithSalt, salt.Length, hash.Length);
        return Convert.ToBase64String(hashWithSalt);
    }
}
