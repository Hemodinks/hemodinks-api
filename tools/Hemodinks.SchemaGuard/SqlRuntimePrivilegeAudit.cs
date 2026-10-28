using System.Data.Common;

namespace Hemodinks.SchemaGuard;

public static class SqlRuntimePrivilegeAudit
{
    public static async Task VerifyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        using var stream = typeof(SqlRuntimePrivilegeAudit).Assembly.GetManifestResourceStream("SqlRuntimePermissionAudit.sql")
            ?? throw new InvalidOperationException("Runtime SQL audit resource is unavailable.");
        using var reader = new StreamReader(stream);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = await reader.ReadToEndAsync(cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
