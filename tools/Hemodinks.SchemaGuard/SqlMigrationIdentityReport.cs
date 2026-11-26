using System.Data.Common;

namespace Hemodinks.SchemaGuard;

// #204: diagnostico somente leitura. Informa se a identidade de migration entra como dbo
// (admin SQL do servidor, admin Entra ou dono do banco), sem imprimir login, servidor ou banco.
public static class SqlMigrationIdentityReport
{
    public const string Query =
        "SELECT CAST(CASE WHEN USER_NAME() = N'dbo' THEN 1 ELSE 0 END AS int), " +
        "CAST(ISNULL(IS_ROLEMEMBER(N'db_owner'), 0) AS int);";

    public sealed record Result(bool MapsToDbo, bool InDbOwnerRole)
    {
        public string Describe() =>
            $"MigrationIdentityMapsToDbo={MapsToDbo.ToString().ToLowerInvariant()} " +
            $"MigrationIdentityInDbOwnerRole={InDbOwnerRole.ToString().ToLowerInvariant()}";

        // dbo = admin do servidor/Entra ou dono do banco: a credencial administrativa ainda esta em uso.
        public string? Warning => MapsToDbo
            ? "Migration identity maps to dbo (server/Entra admin or database owner). Replace it with a dedicated migration user before rotating the administrative credential (#204)."
            : null;
    }

    // So leitura e so booleanos; exit 0 com o relatorio, 2 se nao conseguir ler.
    public static async Task<int> RunAsync(string? connectionString, TextWriter output, TextWriter error,
        TimeSpan? deadline = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await error.WriteLineAsync("Migration identity report skipped: migration connection is not configured.");
            return 2;
        }

        try
        {
            using var timeout = new CancellationTokenSource(deadline ?? TimeSpan.FromMinutes(4));
            Result? result = null;
            await SqlReadRetry.ExecuteAsync(async cancellationToken =>
            {
                await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                result = await ReadAsync(connection, cancellationToken);
                return true;
            }, timeout.Token, (attempt, failure) =>
                output.WriteLine($"Migration identity report retryAfterAttempt={attempt} {SqlFailureDiagnostic.Describe(failure)}"));

            await output.WriteLineAsync(result!.Describe());
            if (result.Warning is { } warning)
                await output.WriteLineAsync("::warning::" + warning);
            return 0;
        }
        catch (Exception failure)
        {
            // Nunca expor connection string, login ou servidor.
            await error.WriteLineAsync($"Migration identity report failed: {SqlFailureDiagnostic.Describe(failure)}");
            return 2;
        }
    }

    public static async Task<Result> ReadAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = Query;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Migration identity report returned no rows.");
        return new Result(reader.GetInt32(0) == 1, reader.GetInt32(1) == 1);
    }
}
