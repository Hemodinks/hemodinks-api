namespace HemodinksAPI.Application.Async;

public sealed record PasswordResetEmailQueueMessage(
    int ClinicaId,
    string Email,
    string Nome,
    string Token,
    DateTime ExpiresAt);

public sealed record FileExportQueueMessage(
    Guid JobId,
    string Resource,
    string Format,
    int RequestedByUserId,
    int RequestedByPerfilId,
    int ClinicaId,
    DateTime RequestedAt,
    IReadOnlyDictionary<string, string?> Filters,
    string ReportType = "completo",
    FileExportSnapshot? Snapshot = null);

// A bounded, already authorized result captured by the API. Workers never query clinical data.
public sealed record FileExportSnapshot(
    IReadOnlyList<string> Columns,
    IReadOnlyList<string?[]> Rows,
    int TotalRows,
    IReadOnlyList<FileExportTypeTotal> Totals);

public sealed record FileExportTypeTotal(
    string TipoCirurgia,
    int Count,
    decimal? HonorariosCirurgiao,
    decimal? HonorariosAuxiliares,
    decimal? RepasseMedico,
    decimal? ValorGlosa);
