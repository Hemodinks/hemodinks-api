using System.Text;
using HemodinksAPI.Application.Security;

namespace HemodinksAPI.Tests;

// #150: TOTP conferido contra os vetores oficiais da RFC 6238 (apendice B, SHA-1).
public sealed class TotpTests
{
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void ComputeCode_matches_rfc6238_sha1_vectors(long unixSeconds, string expected8)
    {
        var step = unixSeconds / Totp.StepSeconds;
        Assert.Equal(expected8, Totp.ComputeCode(RfcSecret, step, 8));
        Assert.Equal(expected8[^6..], Totp.ComputeCode(RfcSecret, step));
    }

    [Fact]
    public void Base32_round_trips_and_matches_rfc4648()
    {
        Assert.Equal("MZXW6YTBOI", Base32.Encode(Encoding.ASCII.GetBytes("foobar")));
        Assert.Equal("foobar", Encoding.ASCII.GetString(Base32.Decode("mzxw 6ytb oi")));
        var secret = Totp.GenerateSecret();
        Assert.Equal(32, secret.Length);
        Assert.Equal(Totp.SecretBytes, Base32.Decode(secret).Length);
        Assert.Throws<FormatException>(() => Base32.Decode("ABC1"));
    }

    [Fact]
    public void TryVerify_accepts_one_step_of_drift_rejects_others_and_replays()
    {
        var secret = Base32.Encode(RfcSecret);
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var step = Totp.CurrentStep(now);
        string Code(long s) => Totp.ComputeCode(RfcSecret, s);

        Assert.True(Totp.TryVerify(secret, Code(step), now, null, out var matched));
        Assert.Equal(step, matched);
        Assert.True(Totp.TryVerify(secret, Code(step - 1), now, null, out _));
        Assert.True(Totp.TryVerify(secret, Code(step + 1), now, null, out _));
        Assert.False(Totp.TryVerify(secret, Code(step - 2), now, null, out _));
        Assert.False(Totp.TryVerify(secret, Code(step + 2), now, null, out _));

        // Anti-replay: passo ja usado (ou anterior) nao vale de novo.
        Assert.False(Totp.TryVerify(secret, Code(step), now, step, out _));
        Assert.False(Totp.TryVerify(secret, Code(step - 1), now, step, out _));
        Assert.True(Totp.TryVerify(secret, Code(step + 1), now, step, out _));

        Assert.True(Totp.TryVerify(secret, $"{Code(step)[..3]} {Code(step)[3..]}", now, null, out _));
        foreach (var invalid in new[] { null, "", "12345", "1234567", "abcdef" })
            Assert.False(Totp.TryVerify(secret, invalid, now, null, out _));
    }

    [Fact]
    public void OtpAuthUri_escapes_issuer_and_account()
    {
        var uri = Totp.BuildOtpAuthUri("HemoDinks", "ana@clinica.com", "ABC");
        Assert.Equal("otpauth://totp/HemoDinks:ana%40clinica.com?secret=ABC&issuer=HemoDinks&algorithm=SHA1&digits=6&period=30", uri);
    }
}
