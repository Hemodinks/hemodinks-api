using Hemodinks.SchemaGuard;

namespace HemodinksAPI.Tests;

public sealed class SqlDeploymentPolicyTests
{
    private const string Runtime = "Server=tcp:synthetic.invalid,1433;Database=synthetic;User ID=runtime;Password=synthetic-value;Encrypt=True";

    [Fact]
    public void Separate_identities_on_same_database_are_allowed_without_connecting()
    {
        SqlDeploymentPolicy.Validate(Runtime, Runtime.Replace("ID=runtime", "ID=migrator"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-connection-string")]
    [InlineData("Server=tcp:synthetic.invalid,1433;Database=synthetic;User ID=runtime;Password=other")]
    [InlineData("Server=tcp:other.invalid,1433;Database=synthetic;User ID=migrator;Password=synthetic-value")]
    [InlineData("Server=tcp:synthetic.invalid,1433;Database=production;User ID=migrator;Password=synthetic-value")]
    [InlineData("Server=tcp:synthetic.invalid,1433;User ID=migrator;Password=synthetic-value")]
    [InlineData("Server=tcp:synthetic.invalid,1433;Database=synthetic;Integrated Security=True")]
    public void Unsafe_or_ambiguous_migration_connection_is_rejected_without_disclosure(string migration)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SqlDeploymentPolicy.Validate(Runtime, migration));
        Assert.DoesNotContain("synthetic-value", error.ToString());
        Assert.DoesNotContain("other.invalid", error.ToString());
        Assert.DoesNotContain("production", error.ToString());
    }
}
