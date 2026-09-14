using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace HemodinksAPI.Tests;

public sealed class SqlPublicClinicDirectoryTests
{
    [Fact]
    public async Task Oversized_input_and_cancelled_requests_do_not_open_connections()
    {
        var directory = new SqlPublicClinicDirectory(new TestingSqlConnectionFactory(
            () => throw new InvalidOperationException("Connection must not be opened")));
        Assert.Empty(await directory.ListActiveAsync(new string('x', 121), CancellationToken.None));
        Assert.Null(await directory.FindActivePhotoReferenceAsync(new string('x', 121), CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.ListActiveAsync(null, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.FindActivePhotoReferenceAsync("publica", cancellation.Token));
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task Directory_restricts_active_rows_projection_search_and_photo_access()
    {
        // Isolated SQL Server database; no application migrations or Full-Text dependency.
        var databaseName = $"HemodinksPublicDirectory_{Guid.NewGuid():N}";
        var connectionString = SqlServerTestConnection.Create(databaseName);
        var masterString = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var master = new SqlConnection(masterString);
        await master.OpenAsync();
        await using var create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{databaseName}]";
        await create.ExecuteNonQueryAsync();
        try
        {
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var setup = connection.CreateCommand();
                setup.CommandText = """
                    CREATE TABLE dbo.Clinicas (
                        Id int NOT NULL PRIMARY KEY, Nome nvarchar(120) NOT NULL,
                        Slug nvarchar(120) NOT NULL, Ativa bit NOT NULL,
                        FotoClinica nvarchar(max) NULL, Cnpj varchar(14) NULL,
                        AssinaturaStatus nvarchar(30) NULL);
                    INSERT dbo.Clinicas VALUES
                        (1, N'Publica %_[]', N'publica', 1, N'private-storage-reference', '12345678901234', N'private-billing'),
                        (2, N'Inativa', N'inativa', 0, N'inactive-photo', NULL, NULL);
                    WITH numbers AS (SELECT 10 AS n UNION ALL SELECT n + 1 FROM numbers WHERE n < 69)
                    INSERT dbo.Clinicas (Id, Nome, Slug, Ativa)
                    SELECT n, CONCAT(N'Clinica ', n), CONCAT(N'clinica-', n), 1 FROM numbers;
                    """;
                await setup.ExecuteNonQueryAsync();
            }

            var directory = new SqlPublicClinicDirectory(new SqlConnectionFactory(connectionString));
            var all = await directory.ListActiveAsync(null, CancellationToken.None);
            Assert.Equal(50, all.Count);
            Assert.DoesNotContain(all, c => c.Id == 2);
            Assert.Equal(all.OrderBy(c => c.Nome).ThenBy(c => c.Id).ToList(), all);
            var special = Assert.Single(await directory.ListActiveAsync(" %_[] ", CancellationToken.None));
            Assert.Equal(1, special.Id);
            Assert.True(special.HasPhoto);
            Assert.Empty(await directory.ListActiveAsync("' OR 1=1 --", CancellationToken.None));
            Assert.Empty(await directory.ListActiveAsync("Inativa", CancellationToken.None));
            Assert.Null(await directory.FindActivePhotoReferenceAsync("inativa", CancellationToken.None));
            Assert.Null(await directory.FindActivePhotoReferenceAsync("publica' OR 1=1 --", CancellationToken.None));
            Assert.Equal("private-storage-reference", await directory.FindActivePhotoReferenceAsync("publica", CancellationToken.None));
            var projection = System.Text.Json.JsonSerializer.Serialize(special);
            Assert.DoesNotContain("private", projection);
            Assert.DoesNotContain("Cnpj", projection);
            Assert.DoesNotContain("Assinatura", projection);
        }
        finally
        {
            await using var drop = master.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
