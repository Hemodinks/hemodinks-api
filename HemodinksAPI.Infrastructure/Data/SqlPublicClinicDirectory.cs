using System.Data;
using System.Data.Common;
using HemodinksAPI.Application.Features.Clinics;

namespace HemodinksAPI.Infrastructure.Data;

/// <summary>Public directory only. Never use this reader for tenant-owned data.</summary>
public sealed class SqlPublicClinicDirectory(ISqlConnectionFactory connections) : IPublicClinicDirectory
{
    public async Task<List<PublicClinicSummary>> ListActiveAsync(string? search, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Both searchable columns are nvarchar(120); longer literal searches cannot match.
        if (search?.Trim().Length > 120)
        {
            return [];
        }
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (50) [Id], [Nome], [Slug],
                CASE WHEN [FotoClinica] IS NOT NULL AND [FotoClinica] <> N'' THEN 1 ELSE 0 END AS [HasPhoto]
            FROM [dbo].[Clinicas]
            WHERE [Ativa] = 1
                AND (@search IS NULL OR CHARINDEX(@search, [Nome]) > 0 OR CHARINDEX(@search, [Slug]) > 0)
            ORDER BY [Nome], [Id];
            """;
        AddTextParameter(command, "@search", string.IsNullOrWhiteSpace(search) ? null : search.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<PublicClinicSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1));
        }
        return result;
    }

    public async Task<string?> FindActivePhotoReferenceAsync(string slug, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (slug.Length > 120)
        {
            return null;
        }
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) [FotoClinica] FROM [dbo].[Clinicas]
            WHERE [Ativa] = 1 AND [Slug] = @slug;
            """;
        AddTextParameter(command, "@slug", slug);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static void AddTextParameter(DbCommand command, string name, string? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Size = 120;
        parameter.Value = (object?)value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
