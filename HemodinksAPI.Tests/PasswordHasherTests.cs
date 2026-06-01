using HemodinksAPI.Api.Utils;

namespace HemodinksAPI.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_WhenPasswordIsValid_ReturnsVerifiableHash()
    {
        var hash = _hasher.HashPassword("Senha@123");

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
}
