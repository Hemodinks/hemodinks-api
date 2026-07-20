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
    IReadOnlyDictionary<string, string?> Filters);
