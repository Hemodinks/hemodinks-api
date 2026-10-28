using Microsoft.Data.SqlClient;

namespace Hemodinks.SchemaGuard;

public static class SqlDeploymentPolicy
{
    public static void Validate(string? runtime, string? migration)
    {
        const string failure = "SQL identity preflight blocked: configure distinct explicit identities on the same server and database.";
        try
        {
            var runtimeOptions = new SqlConnectionStringBuilder(runtime);
            var migrationOptions = new SqlConnectionStringBuilder(migration);
            if (string.IsNullOrWhiteSpace(runtimeOptions.DataSource)
                || string.IsNullOrWhiteSpace(runtimeOptions.InitialCatalog)
                || string.IsNullOrWhiteSpace(runtimeOptions.UserID)
                || string.IsNullOrWhiteSpace(migrationOptions.UserID)
                || runtimeOptions.IntegratedSecurity || migrationOptions.IntegratedSecurity
                || !string.Equals(runtimeOptions.DataSource.Trim(), migrationOptions.DataSource.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(runtimeOptions.InitialCatalog, migrationOptions.InitialCatalog, StringComparison.OrdinalIgnoreCase)
                || string.Equals(runtimeOptions.UserID.Trim(), migrationOptions.UserID.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(failure);
        }
        catch (ArgumentException)
        {
            // Parser exceptions may include supplied values. Do not retain an inner exception.
            throw new InvalidOperationException(failure);
        }
    }
}
