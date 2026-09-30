using Microsoft.Data.SqlClient;

namespace Hemodinks.SchemaGuard;

public static class SqlFailureDiagnostic
{
    public static string Category(int number) => number switch
    {
        -2 => "SqlTimeout",
        18456 or 18452 => "AuthenticationFailed",
        229 or 230 => "PermissionDenied",
        40615 => "FirewallDenied",
        4060 or 40613 => "DatabaseUnavailable",
        10928 or 10929 or 40501 => "CapacityOrThrottling",
        53 or 64 or 233 or 10060 or 10061 or 11001 => "NetworkOrEndpoint",
        _ => "SqlFailure"
    };

    public static string Describe(Exception error) => error switch
    {
        SqlException sql => $"category={Category(sql.Number)} sqlNumber={sql.Number} state={sql.State} severity={sql.Class}",
        OperationCanceledException => "category=CancelledOrDeadlineExceeded",
        ArgumentException => "category=InvalidConfiguration",
        TimeoutException => "category=Timeout",
        _ => "category=UnexpectedFailure"
    };
}
