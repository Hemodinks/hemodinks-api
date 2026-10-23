using Microsoft.Data.SqlClient;

namespace HemodinksAPI.Tests;

internal static class SqlServerTestConnection
{
    private const string ConnectionStringEnvironmentVariable = "HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING";

    public static void RequireConfigured()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable))
            && Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1")
            Assert.Skip("Configure an isolated SQL Server with HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING or opt in to disposable LocalDB with HEMODINKS_TEST_LOCALDB=1.");
    }

    public static string Create(string databaseName)
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            var builder = new SqlConnectionStringBuilder(configuredConnectionString)
            {
                InitialCatalog = databaseName
            };

            return builder.ConnectionString;
        }

        return $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
    }
}
