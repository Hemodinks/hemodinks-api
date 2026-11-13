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

    private const string AfterBaseline = "20261111154833_Schema_ProtectCpfAtRest";

    [Fact]
    public void ExistingDatabaseWithConsolidatedHistoryMatchesBaseline()
    {
        string[] expected = [ConsolidatedMigrations.BaselineId, AfterBaseline];
        var applied = ConsolidatedMigrations.Ids.Concat(expected).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, SchemaHistoryGuard.NormalizeApplied(applied));
        Assert.True(SchemaHistoryGuard.Matches(expected, applied));
    }

    [Fact]
    public void NewDatabaseHistoryIsKeptUnchanged()
    {
        string[] applied = [ConsolidatedMigrations.BaselineId, AfterBaseline];
        Assert.Equal(applied, SchemaHistoryGuard.NormalizeApplied(applied));
    }

    [Fact]
    public void PartialConsolidatedHistoryIsInconsistent()
    {
        var applied = ConsolidatedMigrations.Ids.Skip(1).Append(ConsolidatedMigrations.BaselineId).ToArray();

        Assert.Null(SchemaHistoryGuard.NormalizeApplied(applied));
        Assert.False(SchemaHistoryGuard.Matches([ConsolidatedMigrations.BaselineId], applied));
    }

    [Fact]
    public void ConsolidatedHistoryWithoutBaselineIsInconsistent()
    {
        Assert.Null(SchemaHistoryGuard.NormalizeApplied(ConsolidatedMigrations.Ids));
    }

    [Fact]
    public void UnknownMigrationStillBlocksAfterNormalization()
    {
        string[] expected = [ConsolidatedMigrations.BaselineId];
        var applied = ConsolidatedMigrations.Ids.Append(ConsolidatedMigrations.BaselineId)
            .Append("20990101000000_Unknown").ToArray();

        Assert.False(SchemaHistoryGuard.Matches(expected, applied));
    }

    [Fact]
    public void ConsolidatedIdsAreNotCompiledMigrations()
    {
        var options = new DbContextOptionsBuilder<HemodinksAPI.Infrastructure.Data.AppDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options;
        using var context = new HemodinksAPI.Infrastructure.Data.AppDbContext(
            options, HemodinksAPI.Application.Tenancy.ClinicaContextFactory.CreatePlatform());
        var known = context.Database.GetMigrations().ToArray();

        Assert.Equal(64, ConsolidatedMigrations.Ids.Count);
        Assert.Equal(ConsolidatedMigrations.BaselineId, known[0]);
        Assert.Empty(known.Where(ConsolidatedMigrations.Ids.Contains));
        Assert.All(ConsolidatedMigrations.Ids, id => Assert.True(string.CompareOrdinal(id, known[0]) < 0));
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
