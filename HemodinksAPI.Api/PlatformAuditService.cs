using System.Security.Claims;
using System.Text.Json;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;

namespace HemodinksAPI.Api;

public sealed class PlatformAuditService
{
    private readonly AppDbContext _context;

    public PlatformAuditService(AppDbContext context)
    {
        _context = context;
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

        _context.AuditoriasPlataforma.Add(new AuditoriaPlataforma
        {
            UsuarioGlobalId = globalUserId,
            ClinicaId = clinicId,
            UserId = localUserId,
            Acao = action,
            Recurso = resource,
            EntidadeId = entityId,
            DetalhesJson = auditDetails == null ? null : JsonSerializer.Serialize(auditDetails),
            Ip = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
            RequestId = httpContext.TraceIdentifier,
            Sucesso = success,
            DataCadastro = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}
