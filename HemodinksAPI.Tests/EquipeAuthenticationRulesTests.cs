using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public sealed class EquipeAuthenticationRulesTests
{
    [Theory]
    [InlineData(null, EquipeModosIdentificacao.Pin)]
    [InlineData("pin", EquipeModosIdentificacao.Pin)]
    [InlineData(" Selecao ", EquipeModosIdentificacao.Selecao)]
    [InlineData("Nenhuma", EquipeModosIdentificacao.Nenhuma)]
    public void NormalizeModo_ReturnsSupportedCanonicalValue(string? input, string expected)
    {
        Assert.Equal(expected, EquipeAuthenticationRules.NormalizeModo(input));
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("12a456", false)]
    [InlineData(null, false)]
    public void IsValidPinFormat_RequiresExactlySixDigits(string? pin, bool expected)
    {
        Assert.Equal(expected, EquipeAuthenticationRules.IsValidPinFormat(pin));
    }

    [Fact]
    public void ChallengeTokens_AreRandomAndStoredAsStableHashes()
    {
        var first = EquipeAuthenticationRules.GenerateChallengeToken();
        var second = EquipeAuthenticationRules.GenerateChallengeToken();

        Assert.NotEqual(first, second);
        Assert.Equal(64, EquipeAuthenticationRules.HashChallengeToken(first).Length);
        Assert.Equal(
            EquipeAuthenticationRules.HashChallengeToken(first),
            EquipeAuthenticationRules.HashChallengeToken(first));
    }
}
