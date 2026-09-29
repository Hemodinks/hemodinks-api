using Hemodinks.SchemaGuard;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class SchemaHistoryGuardTests
{
    [Theory]
    [InlineData("a,b", "a,b", true)]
    [InlineData("a,b", "b,a", true)]
    [InlineData("a,b", "a", false)]
    [InlineData("a", "a,b", false)]
    [InlineData("a,b", "a,c", false)]
    [InlineData("a", "", false)]
    [InlineData("", "", false)]
    public void OnlyTheSameNonemptyHistoryAllowsDeployment(string expected, string applied, bool allowed)
    {
        Assert.Equal(allowed, SchemaHistoryGuard.Matches(
            expected.Split(',', StringSplitOptions.RemoveEmptyEntries),
            applied.Split(',', StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public async Task MissingHistoryIsReadWithoutCreatingTables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DbContext>().UseSqlite(connection).Options;
        await using var context = new DbContext(options);
        Assert.False(await SchemaHistoryGuard.CheckAsync(context, CancellationToken.None));
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ConnectionFailureCannotBeTreatedAsCompatibleSchema()
    {
        var options = new DbContextOptionsBuilder<DbContext>()
            .UseSqlite("Data Source=missing-schema-guard.db;Mode=ReadOnly").Options;
        await using var context = new DbContext(options);
        await Assert.ThrowsAsync<SqliteException>(() => SchemaHistoryGuard.CheckAsync(context, CancellationToken.None));
    }
}
