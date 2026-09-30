using Hemodinks.SchemaGuard;
namespace HemodinksAPI.Tests;
public sealed class SqlFailureDiagnosticTests
{
    [Theory]
    [InlineData(-2, "SqlTimeout")]
    [InlineData(18456, "AuthenticationFailed")]
    [InlineData(229, "PermissionDenied")]
    [InlineData(40615, "FirewallDenied")]
    [InlineData(40613, "DatabaseUnavailable")]
    [InlineData(40501, "CapacityOrThrottling")]
    [InlineData(10060, "NetworkOrEndpoint")]
    [InlineData(999, "SqlFailure")]
    public void CategorizesSqlNumbers(int number, string expected)
        => Assert.Equal(expected, SqlFailureDiagnostic.Category(number));

    [Fact]
    public void NeverIncludesExceptionMessageOrInnerSecrets()
    {
        const string secret = "Server=private;Password=private-password";
        Exception[] errors = [new Exception(secret), new ArgumentException(secret),
            new TimeoutException(secret), new OperationCanceledException(secret)];
        foreach (var error in errors)
        {
            var diagnostic = SqlFailureDiagnostic.Describe(error);
            Assert.DoesNotContain("private", diagnostic);
            Assert.StartsWith("category=", diagnostic);
        }
    }
}
