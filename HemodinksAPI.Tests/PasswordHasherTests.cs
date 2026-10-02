using HemodinksAPI.Infrastructure.Utils;
using System.Security.Cryptography;
using HemodinksAPI.Application.Utils;

namespace HemodinksAPI.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Theory]
    [InlineData(10_000, PasswordVerificationResult.SuccessRehashNeeded)]
    [InlineData(210_000, PasswordVerificationResult.SuccessRehashNeeded)]
    [InlineData(600_000, PasswordVerificationResult.Success)]
    [InlineData(800_000, PasswordVerificationResult.Success)]
    public void Verification_ReportsUpgradeOnlyForValidWeakCredentials(int iterations, PasswordVerificationResult expected)
    {
        var hash = PasswordHashTestData.Create(TestPasswords.Valid, iterations);
        Assert.Equal(expected, _hasher.VerifyPasswordWithRehash(TestPasswords.Valid, hash));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyPasswordWithRehash("wrong", hash));
    }

    public static TheoryData<string?> MalformedHashes => new()
    {
        "", new string('A', 129),
        "PBKDF2-SHA512$600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "PBKDF2-SHA256$2147483647$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "PBKDF2-SHA256$2000001$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "PBKDF2-SHA256$9999$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "PBKDF2-SHA256$-600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "PBKDF2-SHA256$+600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "PBKDF2-SHA256$ 600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "PBKDF2-SHA256$600000$$",
        "PBKDF2-SHA256$600000$AAAAAAAAAAAAAAAAAAAAAA==$",
        "PBKDF2-SHA256$600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAA",
        "PBKDF2-SHA256$600000$AAAAAAAAAAAAAAAAAAAAAAA=$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "PBKDF2-SHA256$600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        "PBKDF2-SHA256$600000$!!!!!!!!!!!!!!!!!!!!!!!!$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "PBKDF2-SHA256$600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=$extra"
    };

    [Theory]
    [MemberData(nameof(MalformedHashes))]
    public void Verification_RejectsMalformedInputBeforeDerivation(string? hash) =>
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyPasswordWithRehash(TestPasswords.Valid, hash!));

    [Fact]
    public void Verification_RejectsNullInput()
    {
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyPasswordWithRehash(TestPasswords.Valid, null!));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyPasswordWithRehash(null!, "invalid"));
    }

    [Fact]
    public void PinPolicy_RemainsAtExistingCostWithoutMigration()
    {
        var pins = new PinHasher();
        var hash = pins.HashPin("123456");
        Assert.StartsWith("PBKDF2-SHA256$210000$", hash);
        Assert.True(pins.VerifyPin("123456", hash));
        Assert.False(pins.VerifyPin("654321", hash));
        Assert.True(pins.VerifyPin("123456", PasswordHashTestData.Create("123456", 10_000)));
    }

    [Fact]
    public void HashPassword_WhenPasswordIsValid_ReturnsVerifiableHash()
    {
        var hash = _hasher.HashPassword("TestPassword@123");

        Assert.StartsWith("PBKDF2-SHA256$", hash);
        Assert.Equal("600000", hash.Split('$')[1]);
        Assert.True(_hasher.VerifyPassword("TestPassword@123", hash));
    }

    [Fact]
    public void HashPassword_WhenCalledTwiceForSamePassword_ReturnsDifferentHashes()
    {
        var firstHash = _hasher.HashPassword("TestPassword@123");
        var secondHash = _hasher.HashPassword("TestPassword@123");

        Assert.NotEqual(firstHash, secondHash);
        Assert.True(_hasher.VerifyPassword("TestPassword@123", firstHash));
        Assert.True(_hasher.VerifyPassword("TestPassword@123", secondHash));
    }

    [Fact]
    public void VerifyPassword_WhenPasswordDoesNotMatch_ReturnsFalse()
    {
        var hash = _hasher.HashPassword("TestPassword@123");

        Assert.False(_hasher.VerifyPassword("Senha@456", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("U2VuaGFA")]
    public void VerifyPassword_WhenHashIsInvalid_ReturnsFalse(string invalidHash)
    {
        Assert.False(_hasher.VerifyPassword("TestPassword@123", invalidHash));
    }

    [Fact]
    public void VerifyPassword_WhenHashUsesLegacyFormat_ReturnsTrue()
    {
        var legacyHash = CreateLegacyHash("TestPassword@123");

        Assert.True(_hasher.VerifyPassword("TestPassword@123", legacyHash));
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
