using Hemodinks.SchemaGuard;
using Microsoft.Data.SqlClient;

namespace HemodinksAPI.Tests;

// #204: o relatorio diz se a identidade de migration ainda e administrativa, sem expor nomes.
public sealed class SqlMigrationIdentityReportTests
{
    [Fact]
    public void Describe_contains_only_booleans_and_warns_only_for_dbo()
    {
        var admin = new SqlMigrationIdentityReport.Result(MapsToDbo: true, InDbOwnerRole: true);
        var dedicated = new SqlMigrationIdentityReport.Result(MapsToDbo: false, InDbOwnerRole: false);

        Assert.Equal("MigrationIdentityMapsToDbo=true MigrationIdentityInDbOwnerRole=true", admin.Describe());
        Assert.Equal("MigrationIdentityMapsToDbo=false MigrationIdentityInDbOwnerRole=false", dedicated.Describe());
        Assert.NotNull(admin.Warning);
        Assert.Null(dedicated.Warning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Server=tcp:127.0.0.1,1;Database=banco-segredo;User ID=login-segredo;Password=Senha-Segredo-123;Connect Timeout=2")]
    [InlineData("Server=servidor-segredo;Password=Senha-Segredo-123;Bogus Keyword=1")]
    public async Task Run_fails_without_revealing_connection_details(string? connectionString)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await SqlMigrationIdentityReport.RunAsync(connectionString, output, error, TimeSpan.FromSeconds(30));

        Assert.Equal(2, exitCode);
        var printed = output.ToString() + error;
        Assert.NotEmpty(error.ToString());
        foreach (var secret in new[] { "segredo", "Segredo", "127.0.0.1", "Password" })
            Assert.DoesNotContain(secret, printed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MigrationIdentityMapsToDbo", printed);
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task Report_distinguishes_admin_dedicated_user_and_db_owner_member()
    {
        SqlServerTestConnection.RequireConfigured();
        var name = $"migration204-{Guid.NewGuid():N}";
        await using var master = new SqlConnection(SqlServerTestConnection.Create("master"));
        await master.OpenAsync();
        await Execute(master, $"CREATE DATABASE [{name}]");
        try
        {
            await using var connection = new SqlConnection(SqlServerTestConnection.Create(name));
            await connection.OpenAsync();

            // A conexao de teste e administradora da instancia: entra como dbo.
            Assert.Equal(new SqlMigrationIdentityReport.Result(true, true),
                await SqlMigrationIdentityReport.ReadAsync(connection, CancellationToken.None));

            await Execute(connection, "CREATE USER Migrator204 WITHOUT LOGIN;");
            Assert.Equal(new SqlMigrationIdentityReport.Result(false, false), await ReadAs(connection));

            await Execute(connection, "ALTER ROLE db_owner ADD MEMBER Migrator204;");
            Assert.Equal(new SqlMigrationIdentityReport.Result(false, true), await ReadAs(connection));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await Execute(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        }
    }

    private static async Task<SqlMigrationIdentityReport.Result> ReadAs(SqlConnection connection)
    {
        await Execute(connection, "EXECUTE AS USER = N'Migrator204';");
        try { return await SqlMigrationIdentityReport.ReadAsync(connection, CancellationToken.None); }
        finally { await Execute(connection, "REVERT;"); }
    }

    private static async Task Execute(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
