using System.Security.Claims;
using System.Text.Json;
using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Application.Authentication;

namespace HemodinksAPI.Api;

public sealed class PlatformAuditService
{
    private readonly IPlatformAuditWriter _writer;

    public PlatformAuditService(IPlatformAuditWriter writer)
    {
        _writer = writer;
    }

    public async Task RecordAsync(
        HttpContext httpContext,
        string action,
        string resource,
        string? entityId,
        int? clinicId,
        object? details,
        bool success,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(httpContext.User.FindFirstValue(GlobalIdentityClaimTypes.UsuarioGlobalId), out var globalUserId))
        {
            throw new InvalidOperationException("Identidade global ausente da sessao; auditoria recusada.");
        }

        var localUserId = int.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
            ? parsedUserId
            : (int?)null;
        var equipeId = int.TryParse(httpContext.User.FindFirstValue(GlobalIdentityClaimTypes.EquipeId), out var parsedEquipeId)
            ? parsedEquipeId
            : (int?)null;
        var equipeOperadorId = int.TryParse(httpContext.User.FindFirstValue(GlobalIdentityClaimTypes.EquipeOperadorId), out var parsedOperadorId)
            ? parsedOperadorId
            : (int?)null;
        var auditDetails = equipeId.HasValue
            ? new { EquipeId = equipeId, EquipeOperadorId = equipeOperadorId, Detalhes = details }
            : details;

        await _writer.WriteAsync(new PlatformAuditEntry(
            globalUserId,
            clinicId,
            localUserId,
            action,
            resource,
            entityId,
            auditDetails == null ? null : JsonSerializer.Serialize(auditDetails),
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            httpContext.TraceIdentifier,
            success,
            DateTime.UtcNow), cancellationToken);
    }
}
