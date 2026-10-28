using Microsoft.Data.SqlClient;
using Hemodinks.SchemaGuard;

namespace HemodinksAPI.Tests;

public sealed class SqlRuntimePermissionsTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task Runtime_can_use_data_and_read_history_but_cannot_administer_database()
    {
        await WithDatabase(async connection =>
        {
            await Execute(connection, "CREATE USER Runtime197 WITHOUT LOGIN;");
            await Configure(connection);
            await Configure(connection); // Repeatable without broadening access.
            await Execute(connection, "EXECUTE AS USER = N'Runtime197';");
            try
            {
                await SqlRuntimePrivilegeAudit.VerifyAsync(connection, CancellationToken.None);
                await Execute(connection, "INSERT dbo.RuntimeProbe VALUES (1, 1); UPDATE dbo.RuntimeProbe SET ClinicaId=2 WHERE Id=1;");
                Assert.Equal(2, await Scalar(connection, "SELECT ClinicaId FROM dbo.RuntimeProbe WHERE Id=1"));
                await Execute(connection, "SELECT * FROM dbo.__EFMigrationsHistory; DELETE dbo.RuntimeProbe;");
                foreach (var forbidden in new[]
                {
                    "CREATE TABLE dbo.Forbidden (Id int)",
                    "ALTER TABLE dbo.RuntimeProbe ADD Forbidden int",
                    "DROP TABLE dbo.RuntimeProbe",
                    "CREATE USER Forbidden WITHOUT LOGIN",
                    "ALTER ROLE db_owner ADD MEMBER Runtime197",
                    "GRANT SELECT ON dbo.RuntimeProbe TO public",
                    "INSERT dbo.__EFMigrationsHistory VALUES (N'forbidden', N'10')",
                    "UPDATE dbo.__EFMigrationsHistory SET ProductVersion=N'forbidden'",
                    "DELETE dbo.__EFMigrationsHistory",
                    "TRUNCATE TABLE dbo.__EFMigrationsHistory"
                })
                {
                    var error = await Assert.ThrowsAsync<SqlException>(() => Execute(connection, forbidden));
                    Assert.Contains(error.Number, new[] { 229, 262, 297, 15151, 15247, 1088, 3701 });
                }
            }
            finally { await Execute(connection, "REVERT;"); }

            foreach (var privilege in new[]
            {
                "ALTER ROLE db_owner ADD MEMBER Runtime197",
                "GRANT CREATE TABLE TO Runtime197",
                "GRANT IMPERSONATE ON USER::dbo TO Runtime197",
                "GRANT SELECT ON dbo.RuntimeProbe TO Runtime197 WITH GRANT OPTION",
                "GRANT UPDATE (ProductVersion) ON dbo.__EFMigrationsHistory TO Runtime197"
            })
            {
                // Transaction rollback restores the clean baseline after each escalation.
                await Execute(connection, "BEGIN TRANSACTION; " + privilege + "; EXECUTE AS USER=N'Runtime197';");
                try
                {
                    var error = await Assert.ThrowsAsync<SqlException>(() => SqlRuntimePrivilegeAudit.VerifyAsync(connection, CancellationToken.None));
                    Assert.Equal(51972, error.Number);
                }
                finally { await Execute(connection, "REVERT; IF @@TRANCOUNT > 0 ROLLBACK;"); }
            }
        });
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task Provisioning_refuses_wrong_database_and_existing_privileged_user()
    {
        await WithDatabase(async connection =>
        {
            await Execute(connection, "CREATE USER Runtime197 WITHOUT LOGIN;");
            var error = await Assert.ThrowsAsync<SqlException>(() => Configure(connection, "another-database"));
            Assert.Equal(51970, error.Number);
            Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM sys.database_principals WHERE name=N'hemodinks_runtime'"));
            await Execute(connection, "ALTER ROLE db_datawriter ADD MEMBER Runtime197");
            error = await Assert.ThrowsAsync<SqlException>(() => Configure(connection));
            Assert.Equal(51971, error.Number);
            Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM sys.database_principals WHERE name=N'hemodinks_runtime'"));
            await Execute(connection, "ALTER ROLE db_datawriter DROP MEMBER Runtime197; CREATE ROLE hemodinks_runtime AUTHORIZATION dbo; GRANT CREATE TABLE TO hemodinks_runtime;");
            error = await Assert.ThrowsAsync<SqlException>(() => Configure(connection));
            Assert.Equal(51971, error.Number);
            Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM sys.database_role_members WHERE member_principal_id=USER_ID(N'Runtime197')"));
        });
    }

    private static async Task Configure(SqlConnection connection, string? expected = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key=N'Runtime197ExpectedDatabase', @value=@db; EXEC sys.sp_set_session_context @key=N'Runtime197User', @value=N'Runtime197';";
        command.Parameters.AddWithValue("@db", expected ?? connection.Database);
        await command.ExecuteNonQueryAsync();
        await Execute(connection, ReadScript("ConfigureSqlRuntimeRole.sql"));
    }

    private static string ReadScript(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root.FullName, "scripts", name));
    }

    private static async Task Execute(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> Scalar(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task WithDatabase(Func<SqlConnection, Task> test)
    {
        SqlServerTestConnection.RequireConfigured();
        var name = $"runtime197-{Guid.NewGuid():N}";
        await using var master = new SqlConnection(SqlServerTestConnection.Create("master"));
        await master.OpenAsync();
        await Execute(master, $"CREATE DATABASE [{name}]");
        try
        {
            var options = new SqlConnectionStringBuilder(SqlServerTestConnection.Create(name)) { MultipleActiveResultSets = false };
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            await Execute(connection, "CREATE TABLE dbo.RuntimeProbe (Id int PRIMARY KEY, ClinicaId int NOT NULL); CREATE TABLE dbo.__EFMigrationsHistory (MigrationId nvarchar(150) PRIMARY KEY, ProductVersion nvarchar(32)); INSERT dbo.__EFMigrationsHistory VALUES (N'synthetic', N'10');");
            await test(connection);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await Execute(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        }
    }
}
