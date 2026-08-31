using HemodinksAPI.Application.Auditing;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public enum PlatformResultKind
{
    Ok,
    Created,
    NoContent,
    BadRequest,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record PlatformUseCaseResult(
    PlatformResultKind Kind,
    object? Value = null,
    string? Location = null)
{
    public static PlatformUseCaseResult Ok(object value) => new(PlatformResultKind.Ok, value);
    public static PlatformUseCaseResult Created(string location, object value) =>
        new(PlatformResultKind.Created, value, location);
    public static PlatformUseCaseResult NoContent() => new(PlatformResultKind.NoContent);
    public static PlatformUseCaseResult BadRequest(object? value = null) => new(PlatformResultKind.BadRequest, value);
    public static PlatformUseCaseResult Forbidden() => new(PlatformResultKind.Forbidden);
    public static PlatformUseCaseResult NotFound() => new(PlatformResultKind.NotFound);
    public static PlatformUseCaseResult Conflict(object? value = null) => new(PlatformResultKind.Conflict, value);
}

public sealed record PlatformRequestContext(
    int? UsuarioGlobalId,
    int? UserId,
    int? PerfilId,
    int? ClinicaId,
    int? EquipeId,
    int? EquipeOperadorId,
    string? Ip,
    string? UserAgent,
    string? RequestId);

public sealed class PlatformAuditRecorder(IPlatformAuditWriter writer, TimeProvider timeProvider)
{
    public Task RecordAsync(
        PlatformRequestContext requestContext,
        string action,
        string resource,
        string? entityId,
        int? clinicId,
        object? details,
        bool success,
        CancellationToken cancellationToken)
    {
        var globalUserId = requestContext.UsuarioGlobalId
            ?? throw new InvalidOperationException("Identidade global ausente da sessão; auditoria recusada.");
        var auditDetails = requestContext.EquipeId.HasValue
            ? new
            {
                requestContext.EquipeId,
                requestContext.EquipeOperadorId,
                Detalhes = details
            }
            : details;

        return writer.WriteAsync(new PlatformAuditEntry(
            globalUserId,
            clinicId,
            requestContext.UserId,
            action,
            resource,
            entityId,
            auditDetails == null ? null : System.Text.Json.JsonSerializer.Serialize(auditDetails),
            requestContext.Ip,
            requestContext.UserAgent,
            requestContext.RequestId,
            success,
            timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
    }
}
