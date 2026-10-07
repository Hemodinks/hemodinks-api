using System.Text;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Infrastructure.Security;
using HemodinksAPI.Infrastructure.Seeders;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.Configuration;

namespace HemodinksAPI.Tests;

public class NewPasswordPolicyTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("12345678")]
    [InlineData("iloveyou")]
    [InlineData("qwertyuiop")]
    [InlineData("Senha@123")]
    public void PublicDatasetAndRetiredCredential_AreRejected(string password)
    {
        var error = Assert.Throws<CompromisedPasswordException>(() => TestPasswordPolicy.Instance.Validate(password));
        Assert.Contains("Escolha outra", error.Message);
        Assert.DoesNotContain(password, error.ToString());
    }

    [Theory]
    [InlineData("  o luar ilumina sete barcos azuis  ")]
    [InlineData("café e montanhas ao amanhecer")]
    [InlineData("café e montanhas ao amanhecer")]
    public void Passphrases_AreAcceptedWithoutChangingTheirValue(string password)
    {
        TestPasswordPolicy.Instance.Validate(password);
        var lookup = new RecordingLookup();
        new NewPasswordPolicy(lookup).Validate(password);
        Assert.Equal(password, lookup.Candidate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("        ")]
    public void InvalidInput_DoesNotReachLookup(string? password)
    {
        var lookup = new RecordingLookup();
        Assert.Throws<InvalidOperationException>(() => new NewPasswordPolicy(lookup).Validate(password));
        Assert.Null(lookup.Candidate);
    }

    [Fact]
    public void LengthBounds_AreEnforcedBeforeLookup()
    {
        var lookup = new RecordingLookup();
        var policy = new NewPasswordPolicy(lookup);
        policy.Validate(new string('z', 8));
        policy.Validate(new string('z', 500));
        lookup.Candidate = null;
        Assert.Throws<InvalidOperationException>(() => policy.Validate(new string('z', 501)));
        Assert.Null(lookup.Candidate);
    }

    [Fact]
    public void MissingOrCorruptDataset_FailsClosedWithoutCandidateInError()
    {
        foreach (var lookup in new[] {
            new LocalCompromisedPasswordLookup(() => null),
            new LocalCompromisedPasswordLookup(() => new MemoryStream(Encoding.UTF8.GetBytes("corrupt"))),
            new LocalCompromisedPasswordLookup(() => throw new IOException("must-not-appear")) })
        {
            const string password = "candidate must remain private";
            Assert.Equal(PasswordLookupResult.Unavailable, lookup.Check(password));
            var error = Assert.Throws<PasswordPolicyUnavailableException>(() => new NewPasswordPolicy(lookup).Validate(password));
            Assert.DoesNotContain(password, error.ToString());
            Assert.DoesNotContain("must-not-appear", error.ToString());
            Assert.Null(error.InnerException);
        }
    }

    [Fact]
    public void Dataset_IsSafeForConcurrentLookups()
    {
        var lookup = new LocalCompromisedPasswordLookup();
        Parallel.For(0, 100, _ => Assert.Equal(PasswordLookupResult.Found, lookup.Check("password")));
    }

    [Fact]
    public void AdministrativeSeed_CannotUseBlockedPassword()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Seed:InitialPassword"] = "password" }).Build();
        var seeder = new UserSeeder(TestPasswordPolicy.Instance, new PasswordHasher(), configuration);
        Assert.Throws<CompromisedPasswordException>(() => seeder.GenerateUsers());
    }

    private sealed class RecordingLookup : ICompromisedPasswordLookup
    {
        public string? Candidate;
        public PasswordLookupResult Check(string candidate) { Candidate = candidate; return PasswordLookupResult.NotFound; }
    }
}
